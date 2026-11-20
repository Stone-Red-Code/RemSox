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
- a GUI text editor (Explorer exists now). Explorer itself could gain: copy, move, rename
  (reusing its action row); opening the folder a Terminal or a shortcut is in.
- proper documentation (for users and developers)
- typed render primitives instead of `RenderCommand`'s string-keyed property dictionary, and one
  scene model in `RemSox.Shared` for `CanvasRenderSource` and the viewer's `RemoteScreen`.

bugs:

- A window draws an element that changed after the ones that did not (Window.Flush re-sends its primitives, and both renderers append them), so a background that changes covers the text over it. The Terminal and Explorer re-send that text as a workaround; the renderers should keep each element where it was in the draw order.
- Two servers can listen on one port: `rdp 23` starts while Telnet holds port 23, and then takes the new Telnet connections. A server should fail to start (its `Start` throwing is now reported) when the port is taken.
- The RDP server has no authentication or encryption, although `AesPacketCrypto` and `Pkcs5S2PasswordKeyDeriver` exist in `RemSox.Shared`: anyone who reaches the port controls the desktop.
- The `fr` and `es` keyboard layouts cannot type `<` or `>`: Cosmos's FRStandardLayout and ESStandardLayout have no entry for scancode 0x56, the extra key left of Y/Z on ISO keyboards (the German layout had the same gap and now uses RemSox's own `GermanKeyboardLayout`).
