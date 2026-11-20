todo:

- loadable programs (such as WASM modules, if Cosmos gets userland execution): another kind of
  `ProgramInfo`, opened through a file association like any other file. With them: a manifest
  saying what a program may do (files, network, windows), tied to the permission system, and
  one clean kernel API that built-in programs use and that a WASM host would expose as host
  functions.
- files without an association (no extension, or an unknown one) cannot be opened by typing
  them in a shell (Explorer asks which program should open them, with Open with).
- user accounts: one folder per user in `/home` (`/home/<user>`), where that user's shells
  start, and a personal `/home/<user>/programs` searched before `/programs` in their `path`.
  `/home` then holds only user folders.
  Remote desktop then logs in as a user with that user's password (the handshake only needs
  the user's stored hash in place of the `rdp.password` setting, which goes away), and password
  hashes move out of `settings.conf` into a file of their own that only the system can read.
- a permission system. Once it exists, protect `/system` and the top-level folders from `rm`
  and `mv` (compare paths ignoring case: FAT ignores it, so `/SYSTEM` must not get around it).
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
  scene model in `RemSox.Shared` for `CanvasRenderSource` and the viewer's `RemoteScreen`.

bugs:

- A window draws an element that changed after the ones that did not (Window.Flush re-sends its primitives, and both renderers append them), so a background that changes covers the text over it. The Terminal and Explorer re-send that text as a workaround; the renderers should keep each element where it was in the draw order.
- `rdp.password` is stored as a PBKDF2 hash, but the RDP login proves knowledge of that hash's key, so whoever can read `/system/settings.conf` (anyone with a shell; Telnet has no login) can log in to RDP without knowing the password. An augmented PAKE such as SRP would make the stored value useless for logging in.
- A connection to the RDP port made right after another one closed can wait about 6 s before the server sees it (`TcpListener.Pending` stays false; the main loop is not blocked, and Telnet reconnects at once). Likely in Cosmos's TCP stack; it makes a viewer's reconnect after a wrong password slow.
- The `fr` and `es` keyboard layouts cannot type `<` or `>`: Cosmos's FRStandardLayout and ESStandardLayout have no entry for scancode 0x56, the extra key left of Y/Z on ISO keyboards (the German layout had the same gap and now uses RemSox's own `GermanKeyboardLayout`).
