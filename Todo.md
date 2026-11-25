todo:

- loadable programs (such as WASM modules, if Cosmos gets userland execution): another kind of
  `ProgramInfo`, opened through a file association like any other file. With them: a manifest
  saying what a program may do (files, network, windows), tied to the permission system, and
  one clean kernel API that built-in programs use and that a WASM host would expose as host
  functions.
- files without an association (no extension, or an unknown one) cannot be opened by typing
  them in a shell (Explorer asks which program should open them, with Open with).
- copy files between the host and the VM in the viewer (file transfer); `tools/vm.py push`/`pull`
  do it for tests, for text files only.
- more disks: only the first partition is mounted. Others could go under `/disks/<label>/`
  (check that Cosmos allows a mount inside the FAT root).
- `textedit` has no selection yet: copying, cutting and pasting text (with a clipboard shared
  with Explorer's would be Windows-like), and undo.
- disks made before `textedit` keep `open.txt`, `open.conf` and `open.log = edit` in their
  `settings.conf` (defaults are written there once and never changed); only new disks open
  text files in the GUI editor. Change it with `admin config set --system open.txt textedit`.
- Explorer does not notice files changed elsewhere (F5 shows them).
- proper documentation (for users and developers)
- Explorer's rows show no wash under the pointer (they are shapes, not controls), and a
  button's pressed look and a dragged rounded window are not checked by `tools/vm.py`,
  which cannot hold a mouse button down.
- the Telnet server (the `Cosmos.Network.Telnet` package) has a thread that polls for
  connections every 10 ms, and Cosmos's lock writes four debug lines to serial each time: the
  idle desktop is 3.5% busy with Telnet running, 1.5% without. A Telnet server of RemSox's
  own, polled from its process's `Tick` like `TcpRpcServer`, would cost nothing while idle.
- a YesNt script spinning in a loop got only 15% of the processor, measured while each
  main loop iteration still wrote serial lines (`Kernel.Run` now loops by itself): measure
  again.
- YesNt windows: a shape (circle, line, rectangle) cannot be moved, only removed and drawn
  again (`examples/focus.ynt`), and closing a script's window stops the script, which
  cannot ask first. A slider dragged by the pointer is untested (`tools/vm.py` cannot
  hold a mouse button down).
- YesNt's `%read_key` waits for a whole line and takes its first character, since a shell
  hands programs only lines (`IConsoleInput`); a key at a time needs the shell to pass keys.
- YesNt window statements (`YesNtWindowStatements`) stray from the language's own rules:
  - named `x=` arguments: a quoted `=` in a label switches the line to them and fails;
  - text taken as one word (`ui_label`), not to the end of the line as `ui_set` does, and
    `win_create`'s quoted title is not decoded;
  - values come back through argument-taking tokens (`%ui_value id`, `%ui_clicked id`) and
    hidden "last id" state (`%ui_last_id`) rather than through `%out` (`SetOut`), with
    `ui_wait` giving the element that fired;
  - an unknown window or element id is silently ignored by most statements;
  - `win_invalidate_all` matches anywhere in a line.

  The plan: positional arguments with the text last, style statements (`ui_color`,
  `ui_font_size`, `ui_filled`) for what is optional, results through `%out`, every misuse
  an error.
- disks made before `imageview` and `taskmgr` have no Image Viewer or Task Manager start menu
  entry (default shortcuts are written only into a new start menu folder); add one with a
  `.rsl` in `/system/startmenu` (Ctrl+Shift+Esc opens the Task Manager anyway).
- the image viewer shows an image at most at its own size: no zoom in, no panning.
- a way for a program to show `busy` or `working` over its window while it works (the
  cursors are in the set, `CursorRole.Busy`/`Working`, but nothing asks for them yet).
- animated cursors (`.ani`): Bibata's `busy` and `working` are animated, RemSox keeps their
  first frame.
- wallpaper extras (`docs/design/personalization.md`, "Not now"): "Set as wallpaper" in
  Explorer's menu (the image viewer has it), other ways to fit an image than filling the
  screen, sessions sharing one decoded copy of the same background (each screen keeps its
  own, 4 MB at 1280x800).

bugs:

- `who` shows remote desktop sessions as from `0.0.0.0:<port>`: Cosmos's `RemoteEndPoint` of an accepted socket has no address.
- A connection to the RDP port made right after another one closed can wait about 6 s before the server sees it (`TcpListener.Pending` stays false; the main loop is not blocked, and Telnet reconnects at once). Likely in Cosmos's TCP stack; it makes a viewer's reconnect after a wrong password slow.
- The AltGr key itself (QEMU's `sendkey altgr-...`) types nothing on any layout, while Ctrl+Alt gives the AltGr characters. Not yet checked on a real keyboard: it may be QEMU's sendkey, or Cosmos not turning the right Alt scancode (E0 38) into the layouts' 0x60 AltGr entry.
