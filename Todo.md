todo:

- loadable programs (such as WASM modules, if Cosmos gets userland execution): another kind of
  `ProgramInfo`, opened through a file association like any other file. With them: a manifest
  saying what a program may do (files, network, windows), tied to the permission system, and
  one clean kernel API that built-in programs use and that a WASM host would expose as host
  functions.
- files without an association (no extension, or an unknown one) cannot be opened by typing
  them in a shell (Explorer asks which program should open them, with Open with).
- The YesNt.Interpreter library opens files itself: only the script given to `yesnt` is checked
  (`UserFiles`) first, not other script files a script may load. The interpreter would need a
  hook for opening files that goes through the permissions.
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
- disks made before `imageview` have no Image Viewer start menu entry (default shortcuts are
  written only into a new start menu folder); add one with a `.rsl` in `/system/startmenu`.
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
