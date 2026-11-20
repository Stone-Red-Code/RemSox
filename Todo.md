todo:

- loadable programs (such as WASM modules, if Cosmos gets userland execution): another kind of
  `ProgramInfo`, opened through a file association like any other file. With them: a manifest
  saying what a program may do (files, network, windows), tied to the permission system, and
  one clean kernel API that built-in programs use and that a WASM host would expose as host
  functions.
- files without an association (no extension, or an unknown one) cannot be opened by typing
  them; Explorer will need an "open with" choice, and text files could open in `edit`.
- user accounts: one folder per user in `/home` (`/home/<user>`), where that user's shells
  start, and a personal `/home/<user>/programs` searched before `/programs` in their `path`.
  `/home` then holds only user folders.
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
- basic gui apps like explorer and text editor
- proper documentation (for users and developers)
- typed render primitives instead of `RenderCommand`'s string-keyed property dictionary, and one
  scene model in `RemSox.Shared` for `CanvasRenderSource` and the viewer's `RemoteScreen`.

bugs:

- Two servers can listen on one port: `rdp 23` starts while Telnet holds port 23, and then takes the new Telnet connections. A server should fail to start (its `Start` throwing is now reported) when the port is taken.
- The RDP server has no authentication or encryption, although `AesPacketCrypto` and `Pkcs5S2PasswordKeyDeriver` exist in `RemSox.Shared`: anyone who reaches the port controls the desktop.
- The `fr` and `es` keyboard layouts cannot type `<` or `>`: Cosmos's FRStandardLayout and ESStandardLayout have no entry for scancode 0x56, the extra key left of Y/Z on ISO keyboards (the German layout had the same gap and now uses RemSox's own `GermanKeyboardLayout`).
