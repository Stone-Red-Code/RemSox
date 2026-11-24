# System programs: Task Manager and Settings

Status: `ListView` and the Task Manager's Processes page implemented (order steps 1 and 2);
the rest is proposed. Builds on `settings.md` (account settings over the machine's, scopes),
`theming.md` (roles, controls) and `user-accounts.md` (sessions, `Caller`, `admin`).

Everything these two programs do can already be done in a shell: `ps` and `stop` for
processes, `config` for settings, `service` for services. But a desktop user has to open a
Terminal and know the commands, `ps` shows nothing about how busy a process is, and settings
like the theme, the accent or the keyboard layout are exactly what one expects to click.

## Principles

1. **Same rules as the shell.** A program does what its user could do with the commands, no
   more: it calls the same code (`SystemSettings.SetOwn`/`Set`, `ProcessManager.StopProcess`
   with the same rule as `stop`), so permissions and validation stay in one place.
2. **Elevation as in the shell.** Administrators work without admin rights until they ask.
   Started normally, both programs show the machine's things read-only and say that changing
   them needs `admin`; started with `admin taskmgr` or `admin settings` from a Terminal, the
   process is elevated (`ProgramRegistry.Start` already passes it on) and they can be
   changed. A graphical password prompt (like Windows' UAC) is later work, and would serve
   every program, not just these.
3. **Live.** A setting applies when it is changed, as `config set` does (the theme redraws
   every screen at once); the process list refreshes by itself.
4. **One list control for both.** Both programs, and later Explorer, need rows of columns that
   can be selected and scrolled. That becomes a control, `ListView`, instead of each program
   drawing its rows from shapes.

## The `ListView` control

A `Control` in `UI/GUI/UIEelements/Controls`:

- Columns: a title and a width each, with a header row in `surface` and `text.muted`.
- Rows: a key and the cells' text each (`ListRow`); the program replaces them (`SetRows`) and
  the selection follows the row's key (a process id, a setting's name), so refreshing does not
  lose it. When the selected row is gone, the one now at its place is selected.
- Each row is one text, its cells padded to their columns' widths in characters (the font's
  cells are all one width), so a list costs one primitive per row in view, up to 56.
- What it shows apart from drawing (the rows, selection, view and sorting) is `ListRows`,
  tested without a window.
- One selected row (`selection`, `radius` corners), a wash under the pointer (hover), keys up
  and down and Home/End, mouse wheel and a `ScrollBar` when the rows do not fit.
- `SelectionChanged` and `RowActivated` (double click or Enter) events.
- Clicking a column's title sorts by it (numbers as numbers, ties in the order given), again
  the other way; the title shows `^` or `v`.

## Task Manager (`taskmgr`)

A `UserProgram`, one per session (`OnePerSession`): starting it again brings its window to
the front (`IActivatable`, which `ProgramRegistry.Start` calls instead of refusing), unless
one start is elevated and the other is not, which is refused with "close it first". Started
from the start menu (a default shortcut, on disks whose start menu is new), by name, or with
Ctrl+Shift+Esc on the desktop: the desktop sees every key first (`Screen.KeyPreview`), so it
works whatever window is in front.

    +------------------------------------------------------------+
    | Task Manager                                    _  []  X   |
    | [Processes] [Services]                                     |
    |  Name ^           ID   Account   CPU %   Tick us   Windows  |
    |  Desktop Manager  -2   tester      0.2       0.6        1  |
    | >Explorer         12   tester      1.4       3.1        1  |
    |  Telnet Server     0   system      0.0       0.1        0  |
    |  ...                                                       |
    | 18 processes, taking 1.8% of the main loop  [ Stop process ] |
    +------------------------------------------------------------+

- **Processes** (done): every process (as `ps`): name, id, account (marked `(admin)` when
  elevated), its share of the time since the last refresh (CPU %), how long its ticks take
  of late, and how many windows it has. Refreshed once a second, sorted by name; clicking a
  column header sorts by it. `ProcessMetrics` measures in `Stopwatch` timestamps: each
  process's total tick time and an average tick in fractions of a microsecond, since most
  ticks take a few microseconds or less (whole milliseconds showed 0 everywhere).
- **Stop process** (or Delete) stops the selected one with `stop`'s rule, now
  `Process.DenyStop`: your own processes always; another account's or a service only when
  elevated, else the status line says to use `admin taskmgr`. The desktop stops only on a
  second click ("Really stop"), since that logs its user out.
- **Services**: the service list (`ServiceList`) with each service's state (running or not,
  starts at boot or not) and Start, Stop, and "Start at boot" buttons, all needing elevation,
  as `service` does.
- **Status line**: how busy the processor was over the last second (`CpuMeter`), the share
  the processes' ticks took (`ProcessManager.TotalTickTime`, every tick's time since boot)
  and the process count: `CPU 7.8%, processes 0.0% (3)`. Messages (why a process cannot be
  stopped) replace it for a few seconds.
- **Real CPU usage**: the main loop used to go round without pause, so the processor was
  always busy. Now it idles (`MainLoop.Idle`): an iteration with nothing left to do (no work
  posted to the loop, nobody asking `MainLoop.StayAwake`) halts the processor until 15 ms
  have passed since it began: every second tick of the scheduler's 10 ms timer, about 50
  iterations a second. Input from the mouse or keyboard ends the wait at once
  (`LocalDisplay.HasInput`, checked after each interrupt wakes the processor), so the
  pointer moves with every report from the mouse. It halts with `Power.Halt` (one `hlt`)
  rather than `Thread.Sleep`, which wakes only on the timer's ticks and after two of them,
  so the loop ran about 48 times a second, unevenly, and the pointer jerked. A YesNt script
  asks to stay awake while it runs, so it keeps its pace; awaited work (commands) posts its
  continuations, which keeps the loop awake too. Network polling and drawing wait at most
  about 20 ms.
  `CpuMeter` adds two figures: the main loop's thread is the one the kernel booted on, which
  Cosmos's scheduler takes as its idle thread and never counts, so its busy time is the time
  it did not sleep (`MainLoop.SleptTime`); every other thread runs only while it sleeps, and
  the scheduler counts those (`SchedulerDiagnostics.BusyCpuTimeNs`, Cosmos's public figure;
  `SystemNative_GetCpuUtilization`, which the thread pool uses, reads the same counter). Measured in the VM: QEMU took 99.7% of a host core with the old loop and
  57.5% with the idle one; the desktop with the Task Manager open reads about 8%, and a
  YesNt script spinning in a loop reads 100%.
- **Serial costs**: Cosmos writes debug lines to the serial port, each byte a port write the
  VM stops for. Its loop wrote two around every call of `Kernel.Run`, so `Run` now loops by
  itself and never returns; the Telnet package's listener thread wakes every 10 ms, and
  Cosmos's lock wraps each wake in four more lines. Measured on the idle desktop with the
  Task Manager: about 15% before, 3.5% after, 1.5% with Telnet stopped, and 2% with the serial
  port turned off altogether (`CosmosEnableUART=false`). Sent to a Windows console rather
  than a file (`-serial stdio`), the same lines kept it 80-96% busy, since QEMU holds the VM
  while the console writes.
- **Memory** is in the status line: how much of the memory Cosmos's page allocator manages
  is in use (`MemoryDiagnostics`: total and free pages), in whole megabytes. That is the
  whole machine's, the heap and the display's buffers alike; Cosmos has no figure per
  process, so there is no memory column.

## Settings (`settings`)

A `UserProgram`, a singleton per session. A list of pages on the left, the page on the right,
in the theme's controls. Every control shows the value in effect and where it comes from
(yours, the machine's, the default), as `config list` does, with a "Reset" link where your own
value overrides.

    +------------------------------------------------------------+
    | Settings                                        _  []  X   |
    | Personalize  | Theme                                       |
    | Keyboard     |  (o) Dark   ( ) Light   ( ) mine            |
    | Files        | Accent                                      |
    | Machine      |  [#][#][#][#][#][#]  [#3478F6   ] (Reset)   |
    |              | Wallpaper and lock screen                   |
    |              |  [ Choose with Image Viewer ]               |
    |              | Lock after idle                             |
    |              |  [ 10 ] minutes (0 for never)               |
    +------------------------------------------------------------+

- **Personalize**: `theme` (a radio button per theme found: the built-in ones, the system's
  and your own `.theme` files), `accent` (a row of swatches and a colour box), the wallpaper
  and lock screen (opens the Image Viewer, which sets them), `lock.idle`.
- **Keyboard**: `keyboard.layout`, a radio button per layout.
- **Files**: the file associations (`open.<extension>`) in a `ListView`, with Change and Reset
  for the selected one; the built-in types (`.rsh`, `.rsl`) shown but not changeable.
- **Machine** (administrators only; read-only unless elevated): `login.screen`, `log.file`,
  `path`, `pathext`, and the machine's own values of the account settings, i.e. what applies
  to everyone who did not choose; changes go through `SystemSettings.Set`.

A value that is not valid is refused with the same message `config set` gives, shown under the
control. The program reads the settings again when its window is focused, so a change made in
a Terminal meanwhile shows.

## Not now

- A graphical elevation prompt (Principle 2).
- Accounts and groups pages (`account`, `group`): they need the password prompts done
  graphically too.
- Memory per process: Cosmos counts only the whole machine's.
- A drop-down control: radio buttons do while the lists are short.

## Order

1. `ListView`, with tests for its selection and scrolling logic, tried in `TestProcess` (done).
2. Finer tick times in `ProcessMetrics`, and the Task Manager's Processes page (done).
3. The Task Manager's Services page.
4. Settings: Personalize and Keyboard.
5. Settings: Files and Machine.
