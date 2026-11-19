todo:

- a permission system. Once it exists, protect `/system` and the top-level folders from `rm`
  and `mv` (compare paths ignoring case: FAT ignores it, so `/SYSTEM` must not get around it).
- file types: one table saying what opens which file (`.ynt` → YesNt, `.rsh` → shell,
  else `edit`), shared by Explorer, the start menu and an `open <file>` command.
- copy files between the host and the VM (viewer file transfer, or `push`/`pull` in `tools/vm.py`).
- more disks: only the first partition is mounted. Others could go under `/disks/<label>/`
  (check that Cosmos allows a mount inside the FAT root).
- a `path` setting: folders the shell searches so scripts run by name.
- proper desktop environment: customizable start menu, one file per entry in `/system/startmenu`
  (write them with `SafeFile`)
- remove unnecessary commands (like autostart)
- basic gui apps like explorer and text editor
- proper documentation (for users and developers)

bugs:

- The GUI Terminal has no scrollback: output that scrolls off the top cannot be seen again (no mouse wheel or Page Up).
- Resizing or maximizing a Terminal window while `edit` runs leaves it blank until a key is pressed: full-screen programs only redraw after a key, and ITextScreen has no way to tell them the size changed.
- The `fr` and `es` keyboard layouts cannot type `<` or `>`: Cosmos's FRStandardLayout and ESStandardLayout have no entry for scancode 0x56, the extra key left of Y/Z on ISO keyboards (the German layout had the same gap and now uses RemSox's own `GermanKeyboardLayout`).
