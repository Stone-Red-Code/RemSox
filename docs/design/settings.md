# Settings

Status: implemented. Builds on `user-accounts.md` (profiles, logon sessions) and `permissions.md`
(who may write what, `Caller`).

Today there is one settings file, `/system/config/settings.conf`, for the whole machine. A
preference such as "open `.md` files with `edit`" or the keyboard layout is everyone's, so only
an administrator can change it, and every per-account behaviour so far was added as its own
special case: the per-account start menu folder, and the account's programs folder spliced into
the search path in `Launcher`. This document adds settings of one's own, layered over the
machine's, the way Windows layers HKCU over HKLM.

## Principles

1. **One kind of setting, two places.** A setting is the same key wherever it is set. The
   machine's file says what applies to everyone; an account's file says what it changed for
   itself. Reading a setting for an account takes its own value, else the machine's, else the
   built-in default.
2. **Each setting says where it may be set.** Some settings only make sense for the machine
   (they apply before anyone logs in, or to everyone at once). Those cannot be set per account,
   and `config` says so rather than silently ignoring the value.
3. **Your own settings are your own files.** An account's settings file lives in its profile,
   so the permission rules that already protect the profile protect it: its owner may change
   it without admin rights, nobody else may read it.
4. **Whoever reads a setting says for whom.** Settings are read on one thread that serves every
   session at once, so there is no "current user" to fall back on: a per-account setting is
   read for an account, and the code that needs it already has the caller.

## Which settings

| setting | where | why |
|---------|-------|-----|
| `open.<extension>` | account over machine | a preference: which program opens your files |
| `keyboard.layout` | account over machine | your keyboard; the machine's applies at the login screen |
| `path` | machine, then account | an account adds folders after the machine's, like Windows' user PATH |
| `pathext` | machine | decides what counts as runnable; differing per account only confuses |
| `login.screen` | machine | applies before anyone logs in |
| `lock.idle` | account over machine | minutes without input before a desktop locks itself (0, the default, for never); like Windows' own screen lock, each user may choose |
| `log.file` | machine | the system log is the system's |

`path` is the one that adds instead of replacing: an account's folders are searched after the
machine's, never instead of them. Its default is the account's own programs folder
(`/users/<name>/programs`), which replaces the folder `Launcher` adds in code today.

## Files

- Machine: `/system/config/settings.conf`, as now: it lists every machine and account setting
  with its default, so the file shows what applies to everyone.
- Account: `/users/<name>/config/settings.conf`, holding only what the account changed. It does
  not exist until the account sets something, and `config reset` removes lines from it, so the
  machine's value shows through again. Lines for machine-only settings are reported and
  ignored, like unknown ones.

Both use the same `key = value` format, `#` for comments, and are changed line by line so
comments and order survive. That code moves into a `KeyValueFile` class, which `Shortcut` uses
for `.rsl` files too (it has its own copy of the parser today).

The system reads account files directly (the kernel is trusted); writes go through `UserFiles`
with the caller, so the permission rules decide who may change them, as for any file in a
profile.

Account files are read at the account's first use after boot and kept in memory, so resolving
`open.txt` for a file in Explorer does not read the disk. A change through `config` updates the
copy; a hand edit applies after `config reload`.

## Reading a setting

- `SystemSettings.Get(key)`: the machine's value, for machine settings, and for code that wants
  the value everyone gets (`config get --system`).
- `SystemSettings.Get(key, account)`: the account's valid value, else the machine's, else the
  default. An invalid value in an account's file counts as not set.
- `SystemSettings.GetPathFolders(account)`: the machine's `path` folders followed by the
  account's.

The readers that need an account all have a caller:

| reader | account from |
|--------|--------------|
| `FileTypes.GetOpener`, `CanOpen`, `GetAssociations` | their callers: `Launcher` and Explorer (`Process.Caller`), `config` (`context.Caller`) |
| `Launcher` (path search) | `caller` |
| keyboard layout | the session at the machine (below) |
| `lock.idle` | each desktop's session, checked by `DesktopProcess` |
| `login.screen`, `log.file`, `pathext` | none: machine settings |

## Changing a setting

- `config set <key> <value>` changes your own setting (`SystemSettings.SetOwn`); anyone may, for
  an account setting. For a machine setting it says to use `--system`.
- `config set --system <key> <value>` changes the machine's: administrators only, with `admin`.
- `config reset [--system] <key>`: yours goes away, so the machine's applies again; the
  machine's goes back to its default.
- `config list` shows each setting with its value and where it comes from (`yours`, `machine`,
  `default`), and `config get <key>` the value that applies to you.
- `config reload` re-reads your file and reports what in it is ignored (unknown settings,
  machine settings, invalid values); with `--system` (and `admin`) the machine's.
- Explorer's Open with, with "always" ticked, sets your own `open.<extension>`. It no longer
  needs admin rights.

`SystemSettings.Set` and `Reset` already refuse a caller without admin rights; that check stays
for the machine's file. An account's own file is checked by `UserFiles`.

## Keyboard layout

There is one keyboard and one layout in effect, the local machine's (Telnet and remote desktop
clients send characters, already translated by their own keyboard). So:

- At the login screen (the console's or the login window), the machine's layout applies.
- When a session comes to the machine (a console or login-window login, or a desktop session
  taken back from remote desktop), its account's layout applies; when it ends or moves away,
  the machine's again.
- `config set keyboard.layout` applies at once when the account is the one at the machine.

"At the machine" becomes a property of the logon session (`IsAtMachine`), which the power
commands and the idle-session cleanup also ask today with their own
`Kind ==` checks; they switch to it too. `UserStartup`'s check becomes `RunsStartup` (console and
remote desktop logins).

## Folded in

Three small changes touch the same code and went with this work:

- **The logon session says whether it is at the machine.** `LogonSession.IsAtMachine` (the
  keyboard, shutting down, idle-session cleanup) and `RunsStartup` (the
  startup script) replace `Kind ==` checks in each place.

- **Explorer's Open with goes through `Launcher`.** It re-implements "a command or a foreground
  program opens in a Terminal" today. `Launcher.OpenWith(opener, path, folder, caller)` does it
  once.
- **`.rsh` and `.rsl` show among the file types.** They stay built in (a script runs in your
  shell, a shortcut starts its target), but `FileTypes` lists them as fixed types and `config
  list` shows them with the associations instead of a sentence about them.

## Not now

- Administrators changing another account's settings (`config set --user bob`): an
  administrator can edit the file with `admin edit`.
- Group settings (a layer between machine and account): no setting needs it yet.
- Settings applied while running (a theme, a Terminal font): they come with the settings they
  belong to, as account settings.
