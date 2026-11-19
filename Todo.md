todo:

- finish the standard way to run programs (`ProgramRegistry`: built-in programs by name, and
  a runner per file extension), in this order. Loadable programs (such as WASM modules, if
  Cosmos gets userland execution) should then only need another kind of `ProgramInfo` and a
  runner for their extension.
  1. A `path` setting: folders the shell searches, so `hello` finds `hello.ynt` there.
  2. Start menu shortcuts: one small file per entry in `/system/startmenu`, naming a program
     or file plus arguments and a title (write them with `SafeFile`).
  3. Windows-style file associations: typing any file's path opens it, not only scripts. The
     runner table becomes one association table per extension (`.rsh` → `run`, `.ynt` →
     `yesnt`, `.txt` and unknown text → `edit`), and Explorer's double-click uses the same
     lookup, so the shell and Explorer never disagree. No separate `open` command.
  Later, for loadable programs: a manifest saying what a program may do (files, network,
  windows), tied to the permission system, and one clean kernel API that built-in programs use
  and that a WASM host would expose as host functions.
- a permission system. Once it exists, protect `/system` and the top-level folders from `rm`
  and `mv` (compare paths ignoring case: FAT ignores it, so `/SYSTEM` must not get around it).
- copy files between the host and the VM (viewer file transfer, or `push`/`pull` in `tools/vm.py`).
- more disks: only the first partition is mounted. Others could go under `/disks/<label>/`
  (check that Cosmos allows a mount inside the FAT root).
- remove unnecessary commands (like autostart)
- basic gui apps like explorer and text editor
- proper documentation (for users and developers)

bugs:

- The GUI Terminal has no scrollback: output that scrolls off the top cannot be seen again (no mouse wheel or Page Up).
- Resizing or maximizing a Terminal window while `edit` runs leaves it blank until a key is pressed: full-screen programs only redraw after a key, and ITextScreen has no way to tell them the size changed.
- The `fr` and `es` keyboard layouts cannot type `<` or `>`: Cosmos's FRStandardLayout and ESStandardLayout have no entry for scancode 0x56, the extra key left of Y/Z on ISO keyboards (the German layout had the same gap and now uses RemSox's own `GermanKeyboardLayout`).
