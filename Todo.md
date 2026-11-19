todo:

- loadable programs (such as WASM modules, if Cosmos gets userland execution): another kind of
  `ProgramInfo`, opened through a file association like any other file. With them: a manifest
  saying what a program may do (files, network, windows), tied to the permission system, and
  one clean kernel API that built-in programs use and that a WASM host would expose as host
  functions.
- files without an association (no extension, or an unknown one) cannot be opened by typing
  them; Explorer will need an "open with" choice, and text files could open in `edit`.
- starting something that needs a shell from outside one (a `.rsh` or `.txt` from the start
  menu) opens a Terminal that stays open after the script or editor ends.
- user accounts: one folder per user in `/home` (`/home/<user>`), where that user's shells
  start, and a personal `/home/<user>/programs` searched before `/programs` in their `path`.
  `/home` then holds only user folders.
- a permission system. Once it exists, protect `/system` and the top-level folders from `rm`
  and `mv` (compare paths ignoring case: FAT ignores it, so `/SYSTEM` must not get around it).
- copy files between the host and the VM (viewer file transfer, or `push`/`pull` in `tools/vm.py`).
- more disks: only the first partition is mounted. Others could go under `/disks/<label>/`
  (check that Cosmos allows a mount inside the FAT root).
- the desktop has no message box: a start menu entry that cannot start (missing target) only
  says so in the log, and a broken `.rsl` file is logged each time the menu opens.
- remove unnecessary commands (like autostart)
- basic gui apps like explorer and text editor
- proper documentation (for users and developers)

bugs:

- `stop desktop` leaves the GUI programs it was showing (Terminals, the test window) running in `ps`, with no screen to show them on.
- The GUI Terminal has no scrollback: output that scrolls off the top cannot be seen again (no mouse wheel or Page Up).
- Resizing or maximizing a Terminal window while `edit` runs leaves it blank until a key is pressed: full-screen programs only redraw after a key, and ITextScreen has no way to tell them the size changed.
- The `fr` and `es` keyboard layouts cannot type `<` or `>`: Cosmos's FRStandardLayout and ESStandardLayout have no entry for scancode 0x56, the extra key left of Y/Z on ISO keyboards (the German layout had the same gap and now uses RemSox's own `GermanKeyboardLayout`).
