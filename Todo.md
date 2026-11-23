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
- administrators ending someone else's session (`logout <session>`).
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
- a `ScrollPanel` for GUI apps with more content than fits (Explorer, lists, settings): child
  elements moved by an offset and clipped to the panel, with a `ScrollBar`. The renderer has no
  clipping yet: it needs a clip command in `CanvasRenderSource` and the viewer's `RemoteScreen`
  (fits with the typed render primitives below).
- a GUI text editor (Explorer exists now). Explorer could also open the folder a shortcut's
  target is in, and select several entries at once (copy, move and delete take one today).
- proper documentation (for users and developers)
- typed render primitives instead of `RenderCommand`'s string-keyed property dictionary, and one
  scene model in `RemSox.Shared` for `CanvasRenderSource` and the viewer's `RemoteScreen`
  (each window's primitives and their order already are one: `PrimitiveList`).

bugs:

- Passwords are stored as PBKDF2 hashes, but the RDP login proves knowledge of the hash's key, so whoever gets `/system/secrets/passwords` (only the system may read it, but a copy of the disk image or a backup has it) can log in over RDP as anyone without knowing their password. An augmented PAKE such as SRP would make the stored value useless for logging in.
- Checking a password (console, Telnet, login window, `user password`) runs PBKDF2 on the main loop for a second or two in QEMU, so everything else stops meanwhile; failed logins wait before the next try, and from the network `LoginThrottle` bounds how often that can be forced (about one failed check every 10 s overall), but each check still stops the system while it runs.
- `who` shows remote desktop sessions as from `0.0.0.0:<port>`: Cosmos's `RemoteEndPoint` of an accepted socket has no address.
- A connection to the RDP port made right after another one closed can wait about 6 s before the server sees it (`TcpListener.Pending` stays false; the main loop is not blocked, and Telnet reconnects at once). Likely in Cosmos's TCP stack; it makes a viewer's reconnect after a wrong password slow.
- The `fr` and `es` keyboard layouts cannot type `<` or `>`: Cosmos's FRStandardLayout and ESStandardLayout have no entry for scancode 0x56, the extra key left of Y/Z on ISO keyboards (the German layout had the same gap and now uses RemSox's own `GermanKeyboardLayout`).
