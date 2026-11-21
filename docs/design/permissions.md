# Permissions

Status: implemented. Builds on `user-accounts.md` (accounts, groups, logon
sessions, the disk layout).

Today every logged-in account can read every file (`/system/secrets/passwords` included, which
is enough to log in over remote desktop as anyone), change or delete anything (`rm -r /system`),
stop anyone's processes and services, and change the system's settings. This document decides
who may do what, and where that is checked, so that accounts protect users from each other.

## Principles

1. **The kernel is trusted; permissions protect users from each other.** RemSox has no user mode:
   every command and program is kernel code. Permissions are checked on behalf of the session
   that asked, at a few choke points every command and program goes through. Loadable programs
   (Todo.md) will get only those choke points as their API, so the same checks hold for them.
2. **One place decides.** `Permissions.Check(session, path, access)` answers every file
   question, and file commands, `edit`, Explorer, YesNt, scripts and the launcher ask it before
   touching a user-given path. The system's own code (accounts, settings, logs, services) works
   as `system` and is not checked.
3. **Folders, not files.** FAT32 has no owner or access fields, so rights are given to folders
   by path and inherited by everything below, as Windows inherits folder permissions. Built-in
   rules come from the disk layout; administrators add their own in a rules file.
4. **Deny unless granted.** A path no rule grants anything on is closed to everyone but `system`.
5. **Admins are users until they elevate.** An administrator works with ordinary rights and
   asks for more per command with `sudo`, giving their password, as on Linux (Windows' UAC asks
   the same question with a dialog). A slip such as `rm -r /system` then fails instead of
   destroying the system, and someone at an administrator's unattended shell cannot use admin
   rights without the password.

## Rights

| right | on a folder | on a file |
|-------|-------------|-----------|
| read  | list it, `cd` into it | read it, run it (script, shortcut, YesNt) |
| write | create, rename and delete what is in it | change it |

Deleting or renaming an item needs `write` on the folder it is in. Copying needs `read` on the
source and `write` on the destination folder; moving needs `write` on both folders.

## Who

A rule grants rights to one of:

- an account (`stone_red`),
- a group (`group:dev`; `group:admins` too, but see elevation),
- `everyone`: every logged-in account,
- `owner`: the account a profile belongs to (only meaningful under `/users/<name>`).

`system` may do everything, always. An elevated administrator (below) may do everything except
read `/system/secrets`, which only `system` may.

## Built-in rules

From the disk layout; they cannot be removed, and the rules file can add to them but never
open `/system/secrets`.

| path | read | write |
|------|------|-------|
| `/` | everyone | nobody (elevated admins: new top-level folders) |
| `/system` | everyone | nobody |
| `/system/secrets` | nobody | nobody |
| `/system/logs` | nobody | nobody |
| `/programs` | everyone | nobody |
| `/users` | everyone (the list of profiles) | nobody |
| `/users/<name>` | owner | owner |
| `/temp` | everyone | everyone |

"Nobody" means only `system` and elevated administrators (and for `/system/secrets` only
`system`). So `/system/config` and the start menu are readable by everyone and changed by
admins with `sudo`; profiles are private to their owner; `/temp` is shared scratch space.

## Rules file

`/system/config/permissions` (public, so anyone can see why they may or may not do
something), one rule per line:

```
# path            who            rights
/shared           everyone       read
/shared/dev       group:dev      read,write
/users/bob/share  group:dev      read
```

- The most specific path decides: rules for `/shared/dev` replace those for `/shared` below it.
  Everyone the rules for a path name gets the union of what those rules grant them.
- A rule can only add to the built-in rules of its path, never take away (so the owner always
  keeps their profile, and `/system/secrets` stays closed).
- `perm <path>` shows who may do what there and which rules say so. `perm grant <path> <who>
  <rights>` and `perm revoke <path> <who>` change the file: anywhere for an elevated admin, and
  inside their own profile for its owner, who can share a folder of theirs this way.

## Paths

A rule is about a folder, so every path is turned into one exact spelling before it is checked:

- made absolute against the working directory, with `.`, `..` and repeated `/` removed;
- each part replaced by the name the directory actually stores. Cosmos's FAT driver ignores
  case (`/SYSTEM/CONFIG` works) and also accepts FAT's 8.3 short names (`SETTIN~1.CON` reads
  `settings.conf`, `STARTM~1` lists `startmenu`), so without this a rule for
  `/users/longname12` could be bypassed with `/users/LONGNA~1`. A part that matches no stored
  name exactly (ignoring case) is treated as not existing.

`Permissions` does this itself (`Permissions.Canonicalize`), and the checked, canonical path is
the one the operation then uses, so nothing can change between the check and the use.

## Elevation

- `sudo <command line>` asks for the administrator's own password (not echoed) and runs that one
  command elevated. A correct password is remembered for 5 minutes in that shell, as Linux does,
  so a series of admin commands does not ask every time. Non-admins are refused.
- Elevated, a command may do everything except read `/system/secrets`; programs it starts (a
  service, an editor) are elevated too.
- What needs elevation: changing what only "nobody" may write (above), `config set|reset|reload`,
  account, group and service commands that change something, `perm grant|revoke` outside your
  own profile, stopping other accounts' processes, `logs`, and ending other sessions.
- The desktop later gets "Run as administrator", asking with a dialog.

## Processes, settings and the rest

- `ps` shows every process with the account it runs as; `stop` stops your own account's
  processes, anything else (services, other accounts') needs `sudo`.
- `config` shows the settings to everyone; changing them needs `sudo`.
- `logs` shows the lines of your own processes (each line records the account its process ran
  as, so a startup script's output can still be read after it ended); the whole system log,
  which tells what everyone did, needs `sudo`.
- `shutdown` and `reboot`: anyone logged in at the machine (console or login window), as on
  Windows; from Telnet or remote desktop only with `sudo`.
- Passwords: `passwd` keeps working for everyone, through `AccountStore`, which runs as
  `system`; nobody else can read `/system/secrets`, which also fixes the Todo.md bug that reading
  the password file lets someone log in over remote desktop as anyone.

## Where it is checked

- `Permissions` (new, `Accounts/` or a new `Security/` folder): `Canonicalize`, `Check(session,
  path, access)`, the built-in rules and the rules file.
- The file commands (`ls cd cat touch write mkdir rm cp mv`), `edit` and its save, Explorer
  (every open, list, create, rename, delete, copy, paste), YesNt's file statements, the
  launcher (finding and running a file), shell scripts and shortcuts, `explorer <folder>` and
  `yesnt <file>` when they prepare their arguments.
- `stop`, `logs`, `config`, `shutdown`/`reboot` and the account, group and service commands
  check the session and elevation.
- Explorer shows folders you cannot read but says so when opened, rather than hiding them.

## Not in this change

Per-file rules, denying rules ("everyone but bob"), quotas, and loadable programs with their
own manifest of rights.

## Decided

- Administrators work with ordinary rights and elevate with `sudo`.
- Profiles are private to their owner; administrators reach them only with `sudo`.
- The rules file and `perm` came with the built-in rules, so groups grant something.
- Rules name accounts and groups by id (`account:3`, `group:0`), as everything that records
  "who" does, so renaming one never breaks a rule; `perm` takes and shows names. Paths in the
  file are quoted like a shell line when they contain spaces.
