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
- `vm.py start --telnet` logs in by typing at the console, which only works with
  `login.screen = text`; with the login window it would log in to the desktop instead.
- copy files between the host and the VM (viewer file transfer, or `push`/`pull` in `tools/vm.py`).
- more disks: only the first partition is mounted. Others could go under `/disks/<label>/`
  (check that Cosmos allows a mount inside the FAT root).
- a broken `.rsl` file is only logged (each time the start menu opens), not shown: the menu
  could list it greyed out, or show a message box once.
- file types that never open a Terminal, like Windows' `.pyw`: an association would need
  arguments (`open.ynw = noterminal yesnt`), but `open.<extension>` takes a single program or
  command name today, so each shortcut to a windowed script has to say `run = noterminal`.
- `textedit` has no selection yet: copying, cutting and pasting text (with a clipboard shared
  with Explorer's would be Windows-like), and undo.
- disks made before `textedit` keep `open.txt`, `open.conf` and `open.log = edit` in their
  `settings.conf` (defaults are written there once and never changed); only new disks open
  text files in the GUI editor. Change it with `admin config set --system open.txt textedit`.
- Explorer does not notice files changed elsewhere (F5 shows them).
- proper documentation (for users and developers)

bugs:

- `who` shows remote desktop sessions as from `0.0.0.0:<port>`: Cosmos's `RemoteEndPoint` of an accepted socket has no address.
- A connection to the RDP port made right after another one closed can wait about 6 s before the server sees it (`TcpListener.Pending` stays false; the main loop is not blocked, and Telnet reconnects at once). Likely in Cosmos's TCP stack; it makes a viewer's reconnect after a wrong password slow.
- The `fr` and `es` keyboard layouts cannot type `<` or `>`: Cosmos's FRStandardLayout and ESStandardLayout have no entry for scancode 0x56, the extra key left of Y/Z on ISO keyboards (the German layout had the same gap and now uses RemSox's own `GermanKeyboardLayout`).
