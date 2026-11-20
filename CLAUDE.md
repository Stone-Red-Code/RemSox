# RemSox

A small operating system written in C# on **Cosmos gen3** (`Cosmos.Sdk` 3.0.x, .NET 10, compiled
ahead of time to a bootable x64 ISO with the Limine bootloader). It has a shell (local console,
GUI Terminal windows and Telnet share the same commands), a FAT32 disk with persistent settings,
a window manager / desktop, a remote desktop server and a Windows viewer for it.

## Projects

- `RemSox.Kernel/` – the OS. Entry point `Kernel.cs` (`BeforeRun` registers commands, mounts the
  disk, loads settings, starts networking; `Run` is the main loop that ticks every process).
  - `Processing/` – cooperative processes (`Process`: `Start`/`Tick`/`Stop`), `ProcessManager`,
    `ProcessManifest` (Singleton/System flags), IPC messages.
  - `Programs/` – what a user starts by name, modelled on Windows: shell commands are like
    `cmd`'s internal commands; anything else is resolved by `Launcher` (the one name
    resolver, for the shell and the start menu alike): a built-in program from
    `ProgramRegistry` (RemSox's `System32`; its `Start` is the one place that starts a
    program's process), else a file in the current folder or the `path` folders (trying the
    `pathext` extensions). `FileTypes` says what opens a file: a `.rsh` script runs in a shell
    and a `.rsl` shortcut (`Shortcut`, one per start menu entry in `/system/startmenu`, above
    the built-in Terminal entry that is always there) starts its target, both built in like
    `.bat` and `.lnk`; any other type opens with the command or program its
    `open.<extension>` setting names. `BuiltInPrograms` lists terminal, noterminal, explorer, test,
    yesnt, desktop, telnet, rdp. What needs a shell opens in a Terminal when started from
    outside one; `noterminal <line>` runs it with no window instead (e.g. `run = noterminal`
    in a shortcut). A shell runs a program through `UI/CLI/ProgramRunner`.
  - `Processes/` – the concrete processes: `CliProcess` (local console), `DesktopProcess`
    (desktop + start menu), `TerminalProcess` (GUI Terminal), `NoTerminalProcess` (a windowless
    shell for one line, output to its log; the boot runs `/system/startup.rsh` with it),
    `ExplorerProcess` (file browser window; opens files through `Launcher`),
    `TelnetProcess`, `RemoteDesktopProcess`, `TestProcess`, YesNt.
  - `UI/CLI/` – the shell: `ICommand` + `CommandContext`, `CommandManager`, `ShellSession`,
    `LineEditor`, `.rsh` scripts (`ShellScript`), full-screen programs via `ITextScreen`
    (`TextEditor` = `edit`). Each command is in `UI/CLI/Commands/`.
  - `UI/GUI/` – `Windows/` (`Window`, `WindowManager`), `UIEelements/` (shapes and controls; the
    folder name's typo is real), `Layout/`, `Rendering/` (canvas and network render sources).
  - `FileSystem/` (mounts the primary disk as the root `/`, formats a blank one;
    `SystemPaths` lists every path the system uses, `DiskLayout` creates the folders at boot),
    `Configuration/` (`/system/settings.conf`), `Logging/` (`SystemLog`, per-process loggers),
    `Plugs/` (Cosmos plugs for runtime imports Cosmos lacks).
- `RemSox.Shared/` – code used by both the kernel and the viewer: TCP RPC, AES-GCM packet crypto,
  the remote desktop protocol and render commands.
- `RemSox.Viewer/` – WinForms remote desktop client (`net10.0-windows`, normal .NET).

## Build, run, test

- Build: `cosmos build` in `RemSox.Kernel/` (→ `RemSox.Kernel/output-x64/RemSox.Kernel.iso`).
  A clean C# compile is not enough: always build with `cosmos build`, since AOT/linking errors
  (missing runtime imports, unsupported BCL APIs) only show up there.
- The user's interactive run is `RemSox.Kernel/build-and-run.ps1` (QEMU window, `disk.img`,
  host ports 9999 RDP / 2323 Telnet).
- **For Claude:** use `python tools/vm.py` to boot a headless VM, take screenshots, type keys,
  click, and run shell commands over Telnet. See the `vm` skill (`.claude/skills/vm/SKILL.md`).
  It uses its own disk (`.vm/disk.img`) and ports, so it does not disturb the user's VM.
- There are no unit tests; verify changes by booting the VM and exercising the feature.
- Viewer: `dotnet build RemSox.Viewer` (runs on Windows, connects to the RDP port).

## Cosmos constraints

- Not everything in the BCL works: missing native/runtime imports fail at link time and need a
  plug in `Plugs/` (see `DebuggerPlug`, `SystemNativeExports`). Avoid reflection-heavy APIs.
- Everything runs on one core in a cooperative loop: a process's `Tick` must return quickly; never
  block or busy-wait in it. Commands are `async` and should honour `context.Cancellation` (Ctrl+C).
- One thread owns all state: the main loop (`Processing/MainLoop`, a `SynchronizationContext`).
  Commands run there and resume there after an `await`, so the system's classes take no locks.
  Other threads may only do blocking I/O and hand results over with `MainLoop.Post`: a Telnet
  session's thread (Cosmos ties `Console` to it) only moves text. `MainLoop.AssertMainThread`
  reports a call from the wrong thread on serial.
- Avoid threads that wait on sockets: Cosmos's scheduler is preemptive, so a waiting thread takes
  CPU from the main loop even when idle. Poll from a `Tick` instead (`TcpRpcServer.Poll`). Cosmos
  sockets are not safe to use from two threads at once, and a TCP write blocks for a round trip
  per 536-byte segment.
- Every console write redraws the screen (and goes over the network on Telnet): write whole
  lines/changed rows, not character by character.
- The serial port is the low-level debug channel: `Cosmos.Kernel.System.Diagnostics.Log.WriteString(...)`.
  `SystemLog.Log` goes to memory (`logs` command) and `/system/logs/remsox.log`, not to serial.
- Commands keep no state of their own: one instance serves every shell at once; per-run state lives
  in `CommandContext`.

## Conventions

- File-scoped namespaces matching folders; explicit types (no `var`); `_ =` for discarded results;
  `sealed` classes; primary constructors; XML doc comments (`<summary>`) on public members that
  explain _why_ and behaviour, not restate the name.
- Catch specific exceptions (`when (ex is IOException or ...)`), log via `SystemLog`/`Logger`.
- Save files the system or the user depends on with `SafeFile.WriteAllLines`, never a plain
  `File.WriteAllLines`: a crash mid-write then cannot leave a half-written file.
- New shell commands: class in `UI/CLI/Commands/`, register in `Kernel.BeforeRun`. See the
  `new-command` skill.
- New processes: subclass `Process`, add flags in `ProcessManifest` if singleton/system. To let
  users start it, register a `BuiltInProgram<T>` in `BuiltInPrograms` (for a default start
  menu entry, add it to `Shortcut`'s defaults) and start it with `ProgramRegistry.Start` (or
  by name with `Launcher.Start` from outside a shell), which passes the starter's folder on;
  GUI apps create windows with `CreateWindow`. Only processes the system starts itself (the
  console, autostart) use `ProcessManager.SpawnProcess` directly, with no folder (they start
  in `/home`). Resolve relative paths against `Process.WorkingDirectory`, never the kernel's
  global current directory.
- Commit messages: imperative subject line, then a prose body explaining what changed and why,
  wrapped at ~72 columns. Commit only when asked.
- Planned work and known bugs live in `Todo.md`. Always keep it up to date: when a change
  finishes a todo item or fixes a listed bug, remove it (in the same commit); when you find a
  bug you don't fix, or work is left for later, add it.
