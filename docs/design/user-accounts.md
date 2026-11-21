# User accounts and logon sessions

Status: implemented, apart from what "Later" lists.

RemSox is used only by someone who has logged in, as on Linux and Windows. This document fixes
the parts that every later feature builds on (identity, storage, sessions, who may start what)
so that the permission system, per-user settings and a lock screen can be added later without
changing them.

## Principles

1. **Nothing runs without an identity.** Every process belongs to a logon session, and every
   session to an account. The kernel's own processes belong to the built-in `system` account.
2. **Identity is a number, the name is a label.** Like Linux UIDs and Windows SIDs, an account
   is identified by an id that never changes and is never reused. Anything that records "who"
   (sessions now, file owners and access rules later) stores the id, so renaming an account
   is cheap and a deleted account's leftovers never belong to a new account with the same name.
3. **Public data and secrets live apart.** Which accounts exist is public: any program may list
   them. Password hashes are secret. Two files, as `/etc/passwd` and `/etc/shadow`, so the
   permission system can lock away only the secret one.
4. **A session, not only a user, owns processes.** Logging out ends everything that session
   started, so nothing a user left running survives their logout.
5. **Before a login, only services and the login itself run.** Services (the Telnet and remote
   desktop servers) and the login prompt or login window run as `system`. No desktop, Terminal
   or shell is ever reachable without a login.

## Accounts

| field  | meaning |
|--------|---------|
| id     | user accounts get `0`, `1`, `2`, ... (the highest id ever given + 1, kept in a `#highest id:` line so a deleted account's id is never given again); built-in accounts count down from `-1` (`system`), as the system's processes do |
| name   | 1-32 characters `a-z 0-9 _ -`, starting with a letter; unique ignoring case (FAT folders ignore it) |
| groups | the ids of the account's groups (below) |

- `system` (id -1) cannot log in, has no password and no home folder. It owns the kernel's
  processes: the boot and the services.
- Each account has a profile folder, `/users/<name>` (below); it is not stored, so it cannot
  disagree with the name. Its folders are created with the account, and again at login if
  any is missing. Deleting the account renames its profile to `/users/<name>.deleted` (or
  `.deleted2`, ...), with the permission rules on it: a dot is in no account name, so a new
  account given the same name later starts with an empty profile instead of the old one's
  files, settings and `startup.rsh`. Creating an account moves aside a profile folder of its
  name that is still there (from before this, or a move that failed), and fails if it cannot.
- `admins` may manage accounts now and will pass the permission checks that come later. The
  first account is an admin, and the last admin can be neither deleted nor demoted.

### Groups

A group is a named set of accounts, which permissions will be granted to. Like an account it
has a permanent id: accounts store their groups' ids, so renaming a group costs nothing and a
deleted group's id never comes back. Built-in groups count down from -1 and cannot be deleted;
there is one so far, `admins` (-1), which the system is always in. Groups administrators make
count up from 0.

- `group list` lists every group with its members; `user show <account>` one account's groups.
- `group add <name>` and `group remove <name>` make and delete groups (admins only); deleting one
  takes its members out of it.
- `group add-member <group> <account>` and `group remove-member <group> <account>` change an
  account's groups (admins only). The last admin cannot leave `admins`. As on Linux, a change applies from
  the account's next login: sessions keep the groups they logged in with.

### Disk layout

Folders are grouped by who may read them, so the permission system can protect whole folders
rather than single files:

```
/system/              the machine
    config/           settings.conf, services, accounts, groups  anyone may read
    secrets/          passwords                                 the system only
    logs/
    startmenu/        shortcuts for everyone
/programs/            programs for everyone
/users/<name>/        the account's profile                     its owner (and admins)
    home/             its own files; its shells start here, and `cd` alone goes here
    config/           what the system keeps for it: startup.rsh, startmenu/, settings.conf
    programs/         its own programs, searched after the machine's `path` folders
/temp/                scratch, emptied at boot
```

The account's own files (`home`) are kept apart from what the system manages for it (`config`,
`programs`), so one can be backed up, cleaned out or protected without the other. `system` has
no profile: its home is the root folder.

The start menu shows the shortcuts of `/system/startmenu` (everyone's, managed by admins once
permissions exist) together with those of the account's own `config/startmenu`, as Windows
merges its All Users and per-user Start Menu folders. A new account's own start menu is empty.

### Files

```
/system/config/accounts     id:name:group ids    one line per account, public
/system/config/groups       id:name              one line per group an admin made, public
/system/secrets/passwords   id:hash              one line per account, secret
```

For example:

```
# /system/config/accounts
0:david:-1
1:guest:

# /system/secrets/passwords
0:pbkdf2-sha256$100000$<salt hex>$<key hex>
1:pbkdf2-sha256$100000$<salt hex>$<key hex>
```

- Both are saved with `SafeFile.WriteAllLines` and repaired by `DiskLayout` at boot like
  `settings.conf`.
- The hash format is the existing `PasswordHash` (PBKDF2-HMAC-SHA256, salt and iteration count
  stored with it). When the default iteration count rises, a successful login rehashes the
  password, so stored hashes get stronger without anyone resetting a password.
- Without a disk there are no accounts, so nobody can log in. The console says so.

## Logon sessions

A logon session is one login: who, how and from where.

| field   | meaning |
|---------|---------|
| id      | counts up from 1 per boot; `0` is the system session |
| account | the account id |
| kind    | `console`, `telnet` or `remote desktop` |
| origin  | `tty1`, or the client's address |

- `Process` gains `Session`. A process started from a shell, the desktop, Explorer or a shortcut
  gets its starter's session, passed along the same way the working directory is passed today
  (`ProgramRegistry.Start`, `Launcher.Start`, `Shortcut.Start`). `ShellSession` gets the logon
  session too, and the shell starts in the account's home folder; `cd` alone goes there.
- Ending a session stops every process in it. A session ends on `logout` (or `exit` in its
  first shell), on Telnet disconnect, and on "Log out" in the start menu.
- Servers a user starts by hand belong to that user's session and stop at logout, like a
  Linux program without `nohup`. Servers that should outlive logins are services (below).
- What may run in the system session is decided by the program's kind (below).

### Kinds of programs

`ProgramInfo` gains a `Kind`, which says who may start it:

| kind | examples | runs as | started by |
|------|----------|---------|------------|
| service | `telnet`, `rdp` | `system` | the system at boot; an admin by hand |
| logon | the login prompt, the login window | `system` | the system only (kernel processes, not programs anyone can start by name) |
| user program | `desktop`, `terminal`, `explorer`, `yesnt`, scripts | the logged-in user | a logged-in user only |

- A **service** has no window and no shell of its own: it waits for clients and serves them.
  It runs as `system`, so it keeps running whoever logs in or out, like a Windows service or a
  systemd unit. An admin starting `rdp` by hand starts it in the system session too, not in
  their own, so it does not stop at their logout; a user who is not an admin cannot start one.
  The service list says which start at boot (below).
- A **logon** program is the one thing with a screen that runs before anyone has logged in. It
  can do nothing but check a name and password and then start a session for that account.
  Windows has `winlogon` and LogonUI for this, Linux `getty`/`login` and a display manager.
- Everything else is a **user program** and needs a session of a logged-in account.

### What runs at boot and at login

The startup script older versions ran at boot (`/system/startup.rsh`) is gone: it ran before
anyone logged in, so all it could still do was start services. In its place:

- **The service list**, `/system/config/services`: one service per line with its arguments
  (`rdp 9999`), started at boot in the system session, like systemd units or Windows services
  set to start automatically. The `service` command shows them (`service`), and administrators
  change them: `service enable <name> [arguments]` (starts at boot, and now),
  `service disable <name>` (no longer, and stops it), `service start|stop <name>` (now only;
  `start` without arguments uses the boot arguments). A service that fails at boot is logged
  and shown at the console's login prompt.
- **Each account's startup script**, `/users/<name>/config/startup.rsh`: run with `noterminal` in the
  new session when the account logs in at the machine (console or login window) or by remote
  desktop, like Windows' Startup folder. That is where `desktop` goes for someone who wants it
  after the console login. Telnet logins do not run it: a `desktop` line would take the
  machine's screen for a session at the other end of a network. New accounts get one with
  commented examples.


### Text or graphical login

Both are logon programs over the same check, so which one greets you is only a setting:

- `login.screen = text` (the default): the console prompt (`login:`, `password:`). After login,
  `desktop` starts the desktop, as `startx` does on Linux.
- `login.screen = graphical`: at boot the screen shows a login window (name, password, "Log in")
  instead of the console prompt. Logging in starts the desktop in a new console session for
  that account; "Log out" in the start menu ends it and brings the login window back. The first
  account on an empty disk is created in that window too.

The login window draws with the window manager but is not part of anyone's desktop: no
taskbar or start menu, and it ends as soon as the session starts. The same window can later
serve as the desktop's lock screen.

## Where you log in

**Console.** At boot the console shows `login:` and `password:` (not echoed). With no accounts
on the disk it first asks for the name and password (twice) of the first account, an admin.
After logout the prompt comes back. Three wrong passwords in a row make it wait a few seconds.

**Desktop.** It runs in the session of whoever started it: from the console prompt, or the
login window with `login.screen = graphical`. The start menu gets "Log out", which ends that
session; the login prompt or window comes back.

**Telnet.** The same prompts before any shell exists. Three failures disconnect, and so does
staying at the prompt for 60 s; at most 4 clients may be at the prompt at once, and more are
disconnected as they connect. With no accounts it tells the client to create one at the
console and disconnects.

**Remote desktop.** There is one screen, so there is one desktop:

- The viewer sends a user name first (handshake version 2, below).
- No desktop running: the login starts one in a new remote desktop session for that user.
- Desktop running in a session of the same user: the viewer attaches to it.
- Desktop running for another user: the login is refused ("in use by <name>").
- Closing the viewer does not end the session (as on Windows); "Log out" in the start menu does.
- While a remote session owns the desktop, local keyboard and mouse are ignored and the local
  screen shows that it is in use remotely, so whoever sits at the machine cannot use the remote
  user's desktop.

### Remote desktop handshake, version 2

```
client -> server   [2][name length][name]
server -> client   [2][iterations][salt][server nonce]
client -> server   [2][client nonce][client proof]
server -> client   [1][server proof]  or  [0]  or  [2][reason]
```

`[2]` refuses the login whatever the password: after it was right (the desktop is in use), or
before it is checked, after too many failed logins. At most 8 clients may be in the handshake
at once; more are disconnected as they connect.

The transcript gains the name. For an unknown name the server answers with a fake salt, an
HMAC of the name under a secret made at boot, and refuses the proof: the reply looks the same
for every name, so names cannot be probed. The `rdp.password` setting goes away; `rdp` needs
at least one account instead.

## Checking passwords

PBKDF2 takes one to two seconds in QEMU, on the main loop, for console and Telnet logins (remote
desktop needs none on the server: it compares HMACs under the stored key). To keep repeated
guesses from freezing the system, a failed login waits before the next try (2 s, then longer),
and only one Telnet client is checked at a time.

That wait belongs to one prompt, so from the network reconnecting or opening several
connections would undo it. Telnet and remote desktop therefore also ask `LoginThrottle`, which
counts failures across all connections, before the password is checked (the Telnet server
through the `ILoginGate` it gives its prompts, remote desktop through `IRpcLogins`; the
console's prompt has none). While it says to wait, the login is refused with "Too many failed
logins; try again in N s." without any PBKDF2:

- **Per name:** after 3 failures in a row, each try waits 2 s, doubling up to 5 minutes, until
  a login with the name succeeds or it has no failure for 15 minutes. Names without an account
  count the same, so the answer does not tell which exist.
- **Overall:** failures use up a budget of 5 that refills by one every 10 s; while it is empty
  no network login is tried. This also bounds how much of the main loop failed checks can take.

Someone guessing keeps that account (or, guessing many names, everyone) from logging in over
the network meanwhile, but never longer than these delays after they stop. Logins at the
machine are not counted or limited, so its owner can always get in.

## Commands

Each is one command with an action, as `config` and `service` are, rather than one command per
action: RemSox has its own names, not Linux's. Each action does one thing, and a command
without an action shows its usage instead of picking one.

| command | who | does |
|---------|-----|------|
| `user show [name]` | anyone | an account (yours without a name): its id and groups |
| `user list` | anyone | the accounts and their groups |
| `user add <name> [--admin]` | admins, with `admin` | creates an account; asks for its password twice |
| `user remove <name>` | admins, with `admin` | deletes an account (not your own, not the last admin); its profile is kept as `/users/<name>.deleted` |
| `user password [name]` | anyone for themselves (asks the old password), admins with `admin` for anyone | changes a password |
| `session list` | anyone | the logged-in sessions, marking this shell's |
| `logout` | anyone | ends the session |
| `admin <command>` | admins | runs one command with admin rights (see `permissions.md`) |
| `group list` | anyone | the groups and their members |
| `group add <name>`, `group remove <name>` | admins, with `admin` | makes or deletes a group |
| `group add-member <group> <account>`, `group remove-member <group> <account>` | admins, with `admin` | changes an account's groups, from its next login |

Passwords are always asked for, never given as arguments, so they do not end up in a shell's
history or a script.

## Existing disks

There is no migration code: the one disk in use before this change was moved to this layout
by hand. A disk without accounts asks for the first one at the console on its next boot.

## Later, building on this

- **Permissions**: every check asks "may session S do X to path P"; the answers come from the
  account id and groups. `/system/secrets` is the first folder to be locked away, and each
  profile is its owner's.
- **Elevation**: an admin running something as `system` (like `admin`), asking for the password.
- **Per-user settings** in the profile's `config` folder, over the system-wide `settings.conf`
  (done: see `settings.md`).
- **Lock screen** for the desktop: the login window, shown over a running session.

## Not in this change

Permissions, elevation, per-user settings, renaming accounts, a lock screen, more than one
desktop at a time.
