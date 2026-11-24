# RemSox

A small operating system written in C# on **Cosmos gen3** (`Cosmos.Sdk` 3.0.x, .NET 10, compiled
ahead of time to a bootable x64 ISO with the Limine bootloader). It has user accounts (nothing is
usable without logging in), a shell (local console, GUI Terminal windows and Telnet share the
same commands), a FAT32 disk with persistent settings, a window manager / desktop, a remote
desktop server and a Windows viewer for it.

## Projects

- `RemSox.Kernel/` – the OS. Entry point `Kernel.cs` (`BeforeRun` registers commands, mounts the
  disk, loads settings, starts networking; `Run` is the main loop that ticks every process;
  `MachinePower` shuts down and restarts, saying first who may).
  - `Accounts/` – accounts and logins (design: `docs/design/user-accounts.md`). `Account` (a
    permanent id, a name, group ids; `system` is -1 and built-in accounts count down, users
    count up from 0), `Group` (ids the same way; built-in `admins`, -1, manages accounts),
    `AccountStore` (`/system/config/accounts` public, `/system/secrets/passwords` secret;
    `PasswordHash`es: SRP verifiers of PBKDF2 keys), `GroupStore` (`/system/config/groups`), `LogonSession`/`LogonSessions`
    (one per login: console, Telnet, remote desktop; the kernel's own is
    `LogonSessions.System`; `Kind`/`Origin` say how it logged in and never change, `Place` says
    where it is now: at the machine, remote desktop, Telnet or locked), `Caller` (a session, elevated or not: who asks for something;
    administrators get admin rights only elevated, with `admin`). Every process belongs to a session
    (`Process.Session`), inherited from whatever started it; ending a session stops its processes.
  - `Security/` – permissions (design: `docs/design/permissions.md`). `Permissions` decides who
    may read or write a path (rights belong to folders and are inherited; built-in rules follow
    the disk layout; `Canonicalize` spells every path the one way it is stored, since FAT also
    accepts other cases and 8.3 short names), `PermissionRules` (`/system/config/permissions`,
    the rules admins and owners add with `perm`), `UserFiles` is the file API every command and
    program uses for user-given paths, checking first.
  - `Processing/` – cooperative processes (`Process`: `Start`/`Tick`/`Stop`), `ProcessManager`,
    `ProcessManifest` (Singleton/System flags), IPC messages.
  - `Programs/` – what a user starts by name, modelled on Windows: shell commands are like
    `cmd`'s internal commands; anything else is resolved by `Launcher` (the one name
    resolver, for the shell and the start menu alike): a built-in program from
    `ProgramRegistry` (RemSox's `System32`; its `Start` is the one place that starts a
    program's process), else a file in the current folder or the `path` folders (trying the
    `pathext` extensions). `FileTypes` says what opens a file: a `.rsh` script runs in a shell
    and a `.rsl` shortcut (`Shortcut`, one per start menu entry in `/system/startmenu` for
    everyone and `/users/<name>/config/startmenu` for one account, whose file replaces the
    system's of the same name, above the built-in Terminal
    entry that is always there) starts its target, both built in like
    `.bat` and `.lnk`; any other type opens with the command or program its
    `open.<extension>` setting names, with any arguments given there before the file
    (`open.ynw = noterminal yesnt`). `BuiltInPrograms` lists terminal, noterminal, explorer, textedit,
    imageview, taskmgr, test, yesnt, desktop, telnet, rdp. A program's `Kind` says who may start it: a `UserProgram` runs
    in the starter's session and needs a logged-in user; a `Service` (telnet, rdp) runs in the
    system session and is started at boot from the service list (`ServiceList`,
    `/system/config/services`, the `service` command) or by an administrator. Each account's
    `/users/<name>/config/startup.rsh` runs in its session when it logs in at the machine or by
    remote desktop (`UserStartup`). What needs a shell opens in a Terminal when started from outside one; `noterminal <line>` runs it with no window instead (e.g. `run = noterminal`
    in a shortcut, or in an association). A start menu entry whose `.rsl` is broken shows greyed
    out and says why when clicked. A shell runs a program through `UI/CLI/ProgramRunner`.
  - `Processes/` – the concrete processes: `CliProcess` (local console: `TextLogin`, then a shell
    as that user), `LoginWindowProcess` (the login window, with `login.screen = graphical`; starts
    the desktop), `DesktopProcess` (desktop + start menu: programs, Lock, Log out, Restart, Shut down), `TerminalProcess` (GUI
    Terminal), `NoTerminalProcess` (a windowless shell for one line, output to its log; a user's
    `startup.rsh` runs in it),
    `ExplorerProcess` (file browser window; opens files through `Launcher`; several entries
    can be chosen for copy, cut and delete), `TextEditProcess` (`textedit`, the GUI text editor,
    which opens `.txt`, `.conf` and `.log` by default), `ImageViewProcess` (`imageview`, which
    opens `.bmp` and `.qoi`: fits the image to the window, steps through its folder, and makes it
    your wallpaper or lock screen),
    `TaskManagerProcess` (`taskmgr`, also Ctrl+Shift+Esc: the processes with their share of
    the main loop, stopped by `stop`'s rule, `Process.DenyStop`; one per session, brought to
    the front when started again, `IActivatable`),
    `TelnetProcess` (each client logs in first), `RemoteDesktopProcess` (viewers log in as an
    account and show its one desktop session, taking it from the machine if it runs there, or
    a new one), `TestProcess` (tries the GUI's parts, a `ScrollPanel` among them), YesNt
    (`YesNtInterpreterProcess`: its library's `sleep`, `%read_line` and `%read_key` block, so
    they are replaced by ones that wait without holding up the main loop, reading the lines
    the running shell passes, `IConsoleInput`; `YesNtWindowStatements` gives scripts windows
    and controls, with events: `ui_wait`, `%ui_clicked`, `%ui_value`, `ui_set`; examples
    in `examples/`).
  - `UI/CLI/` – the shell: `ICommand` + `CommandContext`, `CommandManager`, `ShellSession`,
    `LineEditor`, `.rsh` scripts (`ShellScript`), full-screen programs via `ITextScreen`
    (`TextEditor` = `edit`). Each command is in `UI/CLI/Commands/`.
  - `UI/GUI/` – `Windows/` (`Window`, `Screen`: one per logon session, holding its windows, so
    a process's windows open only on its session's screen; a window's corners are rounded,
    composited by both renderers, unless `IsRounded` is false or it is maximized), `UIEelements/` (shapes and controls,
    `ScrollPanel` for content taller than its box, `TextArea` for editing text, `ListView`
    for rows in columns (its logic in `ListRows`) and `Picture` for an image among them;
    an element's `ClipBounds` hides what lies outside it; its colours are `ThemeColor`s, a role
    of the theme or a fixed colour; a control is `IsHovered` and `IsPressed`, set by its window
    as the screen tells it where the pointer is (`Window.SetPointer`); the folder name's typo
    is real),
    `Layout/`, `Rendering/` (`CanvasRenderSource` draws the shared `Scene` on the display, and
    clips with a scratch canvas; `NetworkRenderSource` sends the commands to a viewer),
    `LocalDisplay` (the machine's own display and input: shows the screen of the desktop at the
    machine, or the login window's; each remote desktop viewer shows its own session's),
    `Cursors` (the pointer is always an image: a `.cur` per `CursorRole` in `/system/cursors`,
    written at boot from the Bibata set in `Resources/Cursors`, made by `tools/make_cursors.py`;
    a control's `Cursor` and the window's edges decide which the screen shows; files in an
    account's `/users/<name>/config/cursors` override them one by one), `Wallpapers` (the
    desktop background and lock screen the same way: `desktop` and `lock` images, `.qoi` or
    `.bmp`, in `/system/wallpapers` and `/users/<name>/config/wallpapers`, filled to the
    screen and sent through the `Scene`; the login screen shows the lock screen of whoever
    locked their session at the machine; design: `docs/design/personalization.md`), `Theme`
    and `Themes` (the colours everything is drawn with, one per `ThemeRole`: the built-in
    `dark` and `light`, `.theme` files in `/system/themes` and `/users/<name>/config/themes`
    over a base, chosen with the `theme` and `accent` settings and applied at once; each
    `Screen` has its account's; programs name roles, never colours, for their chrome;
    design: `docs/design/theming.md`).
  - `FileSystem/` (mounts the primary disk as the root `/`, formats a blank one;
    `SystemPaths` lists every path the system uses and shows the layout: `/system/{config,
    secrets,logs,startmenu,cursors,wallpapers,themes}`, `/programs`, `/users/<name>/{home,config,programs}`, `/temp`;
    `DiskLayout` creates the folders at boot),
    `Configuration/` (`SystemSettings`: the machine's `/system/config/settings.conf`, and each
    account's own `config/settings.conf` over it for the settings an account may set
    (`AccountSettings`); read a setting for an account with `SystemSettings.Get(key, account)`;
    design: `docs/design/settings.md`), `Logging/` (`SystemLog`, per-process loggers),
    `Plugs/` (Cosmos plugs for runtime imports Cosmos lacks).
- `RemSox.Shared/` – code used by both the kernel and the viewer: TCP RPC with its login
  (`RpcHandshake`: user name, an SRP-6a login, then AES-GCM per connection), password hashes,
  the remote desktop protocol, and the render commands: a typed class per command and
  primitive (`RenderCommand`, `Primitive`, with an optional clip rectangle; colours are ARGB,
  and fills blend a translucent one; rounded rectangles smooth their corners with
  `RoundedCorners`) and the `Scene` both renderers keep, drawing through `IPrimitiveCanvas`,
  the pointer's `CursorShape` among it, and the background. `Imaging/` reads image files (`ImageFiles`: BMP and Windows `.cur`;
  `Qoi` reads and writes QOI, the wallpapers' format and the network's), since Cosmos has no
  image types.
- `RemSox.Viewer/` – WinForms remote desktop client (`net10.0-windows`, normal .NET).
- `RemSox.Tests/` – xUnit tests (normal .NET) for the logic that needs no Cosmos: the shared code
  by reference, and the kernel's accounts, permissions, login throttle and parsers by linking
  their source files (the kernel itself only builds with Cosmos). `Fakes/` stands in for the
  few Cosmos-bound types those files touch (`FileSystemManager` with no disk mounted, the serial
  `Log`, `ProcessManager`, `UserStartup`); linking a kernel file that needs more means adding
  to the fakes, never Cosmos itself.

## Build, run, test

- Build: `cosmos build` in `RemSox.Kernel/` (→ `RemSox.Kernel/output-x64/RemSox.Kernel.iso`).
  A clean C# compile is not enough: always build with `cosmos build`, since AOT/linking errors
  (missing runtime imports, unsupported BCL APIs) only show up there.
- The user's interactive run is `RemSox.Kernel/build-and-run.ps1` (QEMU window, `disk.img`,
  host ports 9999 RDP / 2323 Telnet).
- **For Claude:** use `python tools/vm.py` to boot a headless VM, take screenshots, type keys,
  click, and run shell commands over Telnet. See the `vm` skill (`.claude/skills/vm/SKILL.md`).
  It uses its own disk (`.vm/disk.img`) and ports, so it does not disturb the user's VM, and
  logs in as its own test account (`VM_USER` in `tools/vm.py`), creating it on a new disk.
- Tests: `dotnet test RemSox.Tests` (seconds). Add tests for logic you change there; whatever
  needs the disk, processes, the screen or the network (`Canonicalize`, account files, the
  servers) is verified by booting the VM and exercising the feature. Tests run with no disk
  mounted: `SystemPaths` are absolute, so code under test must not touch them for real.
- Viewer: `dotnet build RemSox.Viewer` (runs on Windows, connects to the RDP port).

## Cosmos constraints

- Not everything in the BCL works: missing native/runtime imports fail at link time and need a
  plug in `Plugs/` (see `DebuggerPlug`, `SystemNativeExports`). Avoid reflection-heavy APIs.
- Everything runs on one core in a cooperative loop: a process's `Tick` must return quickly; never
  block or busy-wait in it. Commands are `async` and should honour `context.Cancellation` (Ctrl+C).
  The loop idles between iterations (`MainLoop.Idle`: about 50 a second when nothing
  happens, at once on input or posted work), halting the processor; a process with more work than fits in one tick calls
  `MainLoop.StayAwake` to keep the next iteration coming at once.
- One thread owns all state: the main loop (`Processing/MainLoop`, a `SynchronizationContext`).
  Commands run there and resume there after an `await`, so the system's classes take no locks.
  Other threads may only do blocking I/O and hand results over with `MainLoop.Post`: a Telnet
  session's thread (Cosmos ties `Console` to it) only moves text. Slow pure computation
  (password hashing) runs on its own thread with `BackgroundWork.Run`, awaited on the main loop.
  `MainLoop.AssertMainThread` reports a call from the wrong thread on serial.
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
  users start it, register a `BuiltInProgram<T>` in `BuiltInPrograms` (`Kind = Service` for a
  server; for a default start menu entry, add it to `Shortcut`'s defaults) and start it with
  `ProgramRegistry.Start` (or by name with `Launcher.Start` from outside a shell), which passes
  the starter's folder and logon session on; GUI apps create windows with `CreateWindow`. Only
  processes the system starts itself (the console, the login window) use
  `ProcessManager.SpawnProcess` directly; they run in the system session. Resolve relative
  paths against `Process.WorkingDirectory`, never the kernel's global current directory; who
  runs something is `Process.Caller` (a command's: `context.Caller`), which is what starting
  programs and permission checks take. Something only administrators may do checks
  `Caller.DenyUnlessAdmin`, which tells an administrator to use `admin`.
- Touch user-given paths only through `UserFiles` with the caller (`context.Caller`,
  `Process.Caller`), never `File`/`Directory` directly: that is where permissions are checked.
  Only the system's own files (accounts, settings, logs, services) use `File` directly.
- Never take a password as an argument: ask with `context.ReadSecretAsync` (not echoed, not in
  the history). Store passwords only as `PasswordHash`es, through `AccountStore`.
- Commit messages: imperative subject line, then a prose body explaining what changed and why,
  wrapped at ~72 columns. Commit only when asked.
- Commit in logical steps: one commit per feature, fix or refactor, each building on its own
  and carrying its own doc and `Todo.md` updates. Split a large piece of work into several
  such commits (supporting pieces first) rather than one commit that does everything.
- Planned work and known bugs live in `Todo.md`. Always keep it up to date: when a change
  finishes a todo item or fixes a listed bug, remove it (in the same commit); when you find a
  bug you don't fix, or work is left for later, add it.
