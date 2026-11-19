todo:

- rethink file structure (more logical/less unix like)
- proper desktop environment: customizable start menu (read from a path in the file system?)
- remove unnecessary commands (like autostart)
- basic gui apps like explorer and text editor
- proper documentation (for users and developers)

bugs:

- The GUI Terminal has no scrollback: output that scrolls off the top cannot be seen again (no mouse wheel or Page Up).
- Resizing or maximizing a Terminal window while `edit` runs leaves it blank until a key is pressed: full-screen programs only redraw after a key, and ITextScreen has no way to tell them the size changed.
- The `fr` and `es` keyboard layouts cannot type `<` or `>`: Cosmos's FRStandardLayout and ESStandardLayout have no entry for scancode 0x56, the extra key left of Y/Z on ISO keyboards (the German layout had the same gap and now uses RemSox's own `GermanKeyboardLayout`).
