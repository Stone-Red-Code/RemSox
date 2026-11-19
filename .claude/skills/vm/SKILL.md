---
name: vm
description: Build, boot and drive RemSox in a headless QEMU VM to test or debug a change - take screenshots, type keys, click in the GUI, run shell commands over Telnet, read the serial log. Use whenever a change needs to be seen working, to reproduce a bug from Todo.md, or to find out why the kernel crashes or hangs.
---

# Running RemSox in a VM

All of it goes through `python tools/vm.py` from the repository root. The VM is headless, uses
`.vm/disk.img` and host ports 2324 (Telnet), 9998 (RDP), 4444 (QEMU monitor), so it never touches
the user's own `build-and-run.ps1` VM.

## Typical loop

```sh
python tools/vm.py stop                       # if an old VM is still running
python tools/vm.py start --build --telnet     # cosmos build, boot, wait for the shell, telnet-start
python tools/vm.py telnet "ls /"              # run a shell command, prints its output
python tools/vm.py screenshot                 # writes .vm/screen.png - then Read it
python tools/vm.py stop
```

- `start` options: `--build` (run `cosmos build` first; do this after every code change),
  `--telnet` (types `telnet-start` at the console once the kernel reaches its main loop),
  `--fresh-disk` (blank disk: tests first-boot formatting and default files), `--no-disk` (no
  disk attached at all: tests that file commands say so), `--window` (also
  show a QEMU window to the user).
- A build takes about a minute; run it with a long timeout. Read build errors carefully: Cosmos
  link errors about missing imports usually mean an unsupported BCL API (see CLAUDE.md).
- `telnet "cmd"` opens a fresh session per call (so `cd` does not carry over), waits until output
  has been quiet for `--idle` seconds (default 1.5) and gives up after `--timeout` (default 20).
  Raise `--idle` for slow commands. Lines wrap at 80 columns.
- Interactive / full-screen programs (`edit`, prompts that ask y/n) are easier on the local console:
  `type "edit /a.txt\n"`, `key ctrl-s`, `key ctrl-q`, then `screenshot`.

## Console and GUI

- `type "text\n"` types at the local console or the focused window (US layout; `\n` = Enter, `\t` = Tab).
- `key ctrl-c`, `key up up ret`, `key alt-f4` - QEMU `sendkey` names.
- `click X Y [--button right] [--double]`, `move X Y` - screen pixels, exactly as in the
  1280x800 screenshot. The pointer is homed to the corner first, so it is accurate.
- GUI: `telnet "start-gui"`, wait ~3 s, `screenshot`. The taskbar is at the bottom; `Start` is
  around (34, 781). A new window takes keyboard focus, so you can type into it straight away;
  click another window first to type there. Title bar buttons (minimize, maximize, close) sit at
  the right end of the title bar, 20 px apart: for the default Terminal at (240, 240), roughly
  (588, 249), (608, 249) and (628, 249).
- Always look at the screenshot (Read the PNG) rather than assuming what happened.

## Logs and crashes

- `serial --tail 50` - the serial port: Cosmos boot output, kernel exceptions/panics and anything
  written with `Cosmos.Kernel.System.Diagnostics.Log.WriteString`. The per-tick noise lines are
  filtered out (`--raw` shows them); `--grep REGEX` filters further.
- `telnet "logs"` - the RemSox system log (process starts, errors logged via `SystemLog`); also
  persisted at `/system/logs/remsox.log` (`telnet "cat /system/logs/remsox.log"`).
- If the kernel crashes, the screenshot and the end of the serial log show it. If it hangs,
  `monitor "info registers"` and the last serial lines tell you where. For temporary tracing,
  add `Log.WriteString(...)` calls, rebuild, reproduce, then remove them.
- `monitor "<hmp command>"` passes anything to the QEMU monitor (`info status`, `system_reset`, ...).
- `wait-serial "text" --timeout 60` blocks until text appears on the serial port.

## Clean up

Stop the VM when done (`python tools/vm.py stop`). `.vm/` is git-ignored; the serial log grows by
a few MB per minute in debug builds and is reset on each `start`.
