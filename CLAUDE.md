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
  - `Processes/` – the concrete processes: `CliProcess` (local console), `DesktopProcess`
    (desktop + start menu), `TerminalProcess` (GUI Terminal), `TelnetProcess`,
    `RemoteDesktopProcess`, `AutostartProcess` (runs `/mnt/etc/init.rsh`), `TestProcess`, YesNt.
  - `UI/CLI/` – the shell: `ICommand` + `CommandContext`, `CommandManager`, `ShellSession`,
    `LineEditor`, `.rsh` scripts (`ShellScript`), full-screen programs via `ITextScreen`
    (`TextEditor` = `edit`). Each command is in `UI/CLI/Commands/`.
  - `UI/GUI/` – `Windows/` (`Window`, `WindowManager`), `UIEelements/` (shapes and controls; the
    folder name's typo is real), `Layout/`, `Rendering/` (canvas and network render sources).
  - `FileSystem/` (mounts the primary disk at `/mnt`, formats a blank one), `Configuration/`
    (`/mnt/etc/remsox.conf`), `Logging/` (`SystemLog`, per-process loggers), `Plugs/` (Cosmos
    plugs for runtime imports Cosmos lacks).
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
- Sockets are not safe to use from two threads at once (`TcpRpcBase.SerializeSocketIo`).
- Every console write redraws the screen (and goes over the network on Telnet): write whole
  lines/changed rows, not character by character.
- The serial port is the low-level debug channel: `Cosmos.Kernel.System.Diagnostics.Log.WriteString(...)`.
  `SystemLog.Log` goes to memory (`logs` command) and `/mnt/var/log/remsox.log`, not to serial.
- Commands keep no state of their own: one instance serves every shell at once; per-run state lives
  in `CommandContext`.

## Conventions

- File-scoped namespaces matching folders; explicit types (no `var`); `_ =` for discarded results;
  `sealed` classes; primary constructors; XML doc comments (`<summary>`) on public members that
  explain _why_ and behaviour, not restate the name.
- Catch specific exceptions (`when (ex is IOException or ...)`), log via `SystemLog`/`Logger`.
- New shell commands: class in `UI/CLI/Commands/`, register in `Kernel.BeforeRun`. See the
  `new-command` skill.
- New processes: subclass `Process`, add flags in `ProcessManifest` if singleton/system; GUI
  apps create windows with `CreateWindow` and are launched from `DesktopProcess`'s start menu
  and/or `spawn` (`ProcessCommands.cs`).
- Commit messages: imperative subject line, then a prose body explaining what changed and why,
  wrapped at ~72 columns. Commit only when asked.
- Planned work and known bugs live in `Todo.md`. Always keep it up to date: when a change
  finishes a todo item or fixes a listed bug, remove it (in the same commit); when you find a
  bug you don't fix, or work is left for later, add it.
