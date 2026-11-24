# Personalization: wallpaper, lock screen and cursor

Status: implemented. Builds on `user-accounts.md` (profiles, logon sessions) and
`permissions.md` (reading a user-given file with the caller).

Each account chooses its own desktop background, lock screen and pointer, as on Windows. All
three are image files: the system's in `/system/wallpapers` and `/system/cursors`, written at
boot from copies built into the kernel, and an account's own in its profile's
`config/wallpapers` and `config/cursors`, which replace the system's file by file. There is
no setting for any of them: dropping a file in is all there is to it.

## Principles

1. **Per screen, so per session.** A `Screen` belongs to one logon session, so what it shows
   is that session's account's choice. The login window's screen belongs to the system and
   shows the system's images, except the lock screen of whoever just locked their session at
   the machine.
2. **The scene carries it.** The background and the cursor shape become part of the `Scene`,
   set by render commands like everything else on the screen. Both renderers then show the
   same thing from the same model, and remote desktop needs no code of its own.
3. **No cost per frame.** Decoding and scaling happen once, when the choice changes or a
   screen is shown. Compositing copies a ready buffer, no more work than clearing the screen.
4. **Always an image.** The pointer and the wallpaper always come from image files, never
   from shapes drawn in code. What ships with RemSox is image files too, built into the
   kernel and copied to the disk, where they can be replaced.
5. **An account's own images are user-given files.** They are read through `UserFiles` with
   the caller of the session they are for. Otherwise a user could make a secret file their
   wallpaper and have its bytes drawn as pixels.

## The scene

Two render commands in `RemSox.Shared`, kept by `Scene`:

- `SetCursorShapeCommand`: a `CursorShape`, ARGB pixels and a hotspot, sent raw (a 22x22
  cursor is 2 KB). It raised `RpcHandshake.Version` to 6.
- `SetBackgroundCommand`: a colour, and an image already scaled to the screen or none. The
  image goes over the network as QOI. It raised the version to 7.

`Scene` keeps the current cursor shape and background and reports a change of either
(`SceneChange.CursorShape`; `SceneChange.Background`, which has the screen composited again).
`Screen` sends what it has not sent yet in its update, and `InvalidateAll` (called when a
display starts showing the screen: `LocalDisplay` switching screens, a viewer connecting)
makes it send them again. Until a screen sent a cursor shape, renderers draw no pointer;
until it sent a background, the colour shows.

The wallpaper is not a background-layer window with one big image primitive. That would cost
a full-screen window canvas and the per-window compositing on every frame, for something that
never moves.

### Rendering

The screen scales the image once to fill the screen (`PixelImage.ScaleToFill`: keeping its
proportions, cropping the overflow evenly, nearest neighbour), so the renderers only copy it.
`CanvasRenderSource.CompositeScene` copies it with `DrawArray` instead of clearing the screen
(it keeps it as opaque `int` pixels, made once when it arrives); the viewer keeps a `Bitmap` of
it. An image of another size than the screen, or none, shows the colour.

## Images

Cosmos has no image types, so `RemSox.Shared/Imaging` has its own, tested in `RemSox.Tests`:
`PixelImage` (ARGB pixels, not premultiplied) and `ImageFiles`, which reads:

- **BMP** (done): uncompressed, 1, 4 or 8 bits per pixel with a palette, 24 bits, or 32 bits
  with alpha (or with bit masks): what people have or can export.
- **CUR** (done): Windows cursors, the same images plus a hotspot and a transparency mask, so
  classic Windows cursors work. Cursors holding a PNG are refused.
- **QOI** (`Qoi`): the Quite OK Image format, lossless and a page of code to read and write,
  yet several times smaller than raw pixels for smooth images. RemSox ships its wallpapers in
  it and sends backgrounds to viewers in it.

`ImageFiles.Read` tells BMP and QOI apart by their first bytes.

A full-screen image is megabytes of pixels, and a Cosmos TCP write waits a round trip per 536
bytes, so a background goes to a viewer as QOI of the screen-sized image, once per connection
and change (`NetworkRenderSource` keeps only the newest not yet sent, and sends it after the
windows). The built-in images are 54 KB (desktop) and 118 KB (lock) at 1920x1080. A
photograph compresses far less; if that proves too slow, the next step is sending a hash
first and letting the viewer keep a cache on disk.

Each screen decodes its own background. Sessions using the same image do not share one copy
in memory yet: a 1280x800 background is 4 MB per screen.

## Cursor

A `CursorShape` (`RemSox.Shared`) is an image of at most 256x256 with a hotspot, the pixel
that points. It is always read from a `.cur` file (`CursorShape.FromCursorFile`); there are
no cursors drawn in code.

### The set

RemSox ships a full cursor set, one `.cur` file per Windows pointer role, in
`/system/cursors` (`SystemPaths.CursorsFolder`), one per `CursorRole`:

| file | role |
|------|------|
| `arrow` | normal select (the default pointer) |
| `help` | help select |
| `working` | working in background |
| `busy` | busy |
| `crosshair` | precision select |
| `text` | text select |
| `pen` | handwriting |
| `unavailable` | unavailable |
| `resize-ns`, `resize-ew` | vertical and horizontal resize |
| `resize-nwse`, `resize-nesw` | diagonal resize |
| `move` | move |
| `alternate` | alternate select |
| `link` | link select |
| `grab`, `grabbing` | pan, and grabbing |
| `zoom-in`, `zoom-out`, `person`, `pin` | more Bibata has |

The images are [Bibata](https://github.com/ful1e5/Bibata_Cursor) Modern Ice (white with a
black outline, rounded), by Abdulkaiz Khatri, GPL-3.0 like RemSox
(`Resources/Cursors/Bibata-LICENSE.txt`). It was chosen for covering every Windows role,
being widely used and maintained, and having SVG sources whose colours can be changed.
`tools/make_cursors.py` makes the files from the PNGs in Bibata's `bitmaps.zip` (a release
download): each scaled to 22 pixels, as Bibata's own Windows build does, with Bibata's
hotspots, as a 32-bit image (never a PNG inside). `busy` and `working` are animated in
Bibata; they keep their first frame until RemSox reads animated cursors.

The kernel carries the set as resources (`Resources/Cursors`). `DiskLayout` writes each file
that is missing on the disk, so a replaced cursor stays and a deleted one comes back. A
cursor is read once at its first use (`Cursors.Get(role)`), from
the disk, else from the built-in copy (no disk, or a file that cannot be read). Replacing a
file changes that pointer for everyone from the next boot.

### Drawing

- **Software cursor** (`CanvasRenderSource`): the pixels under the cursor's rectangle (its
  size, placed by the hotspot) are saved before it is drawn and put back when it moves, so a
  move does not composite the scene again. Translucent pixels (the edges, the shadow) are
  blended over the scene.
- **Hardware cursor** (displays whose driver has one): the shape is defined with its hotspot,
  padded into the 32 or 64 pixel square a cursor plane takes when the exact size is refused,
  and defined again when the shape changes. A shape the display refuses is drawn in software.
  The pixels are passed as straight ARGB; whether a driver wants them premultiplied is not
  known (Cosmos documents no format). It only changes the half-transparent edge pixels.
- **Viewer**: draws the shape from a `Bitmap` made once per shape.

### Using the set

Who wants which cursor, as on Windows:

- A control says what it shows over itself (`Control.Cursor`): the normal pointer, `text`
  for `TextBox` and `TextArea`.
- A window (`Window.GetCursorAt`) shows a resize pointer on an edge or corner it can be
  resized by, the normal pointer on its title bar and buttons, and over its content what the
  control under the pointer wants. While it is dragged or resized, it shows `move` or that
  resize wherever the pointer goes; while a press is held on a control (a text selection
  dragged out of its box), that control's cursor.
- The screen (`Screen.GetCursorAt`) asks the window a press is held on, else the top window
  under the pointer, else shows the normal pointer on the desktop. It chooses whenever the
  mouse did something, and every few ticks anyway, since windows open, close and change under
  a still pointer; it sends `SetCursorShapeCommand` only when the shape changed.

A new shape costs little. The software cursor puts back the pixels under the old shape and
draws the new one, without compositing the scene again. A viewer gets only the newest shape
not sent yet (`NetworkRenderSource` keeps it like the position): about 2 KB, four TCP
segments, as the pointer crosses window edges and text boxes. If that proves too slow, the
viewer can keep the shapes it was sent and the kernel send which one by role.

Not used yet: `busy` and `working` (a program telling its window it is working), `link`
(RemSox has no links), and the rest of the set.

### Own cursors

Each account has a cursors folder, `/users/<name>/config/cursors` (`Account.CursorsFolder`),
next to its own start menu folder and made with it (`AccountStore.EnsureProfile`). A `.cur`
file there named like one of the set (`arrow.cur`, `text.cur`, ...) replaces that cursor for
the account; whatever the folder lacks comes from `/system/cursors`. So changing only the
normal pointer takes one file:

    cp mypointer.cur /users/<name>/config/cursors/arrow.cur
    config reload

It is a folder rather than a setting naming one, the way the start menu layers
`/users/<name>/config/startmenu` over `/system/startmenu`: dropping a file in is all there
is to it, with no path to type or get wrong. A setting would only add switching between
whole sets kept elsewhere, which copying files does too.

- Each screen reads its session's account's cursors, with the session's rights, and keeps
  them once read (`Screen.GetCursorShape`). The login window's session is the system's, which
  has no folder: it shows the system's set.
- A file that cannot be read or is not a cursor is logged as the account's entry (its own
  `logs` shows it), and the system's cursor shows instead.
- Files put in the folder show from the next login, or at once after `config reload`, which
  has the account's screens forget the cursors they read (`Screen.ReloadPersonalization`). Nothing
  watches the folder: that would mean reading the disk now and then for a rare change.
- Everyone's cursors change by replacing files in `/system/cursors` (administrators, from the
  next boot).

## Wallpapers and lock screen

Two background roles (`WallpaperRole`), one image file each, `.qoi` or `.bmp`:

| file | shows |
|------|-------|
| `desktop` | behind a desktop's windows |
| `lock` | behind the login window: the screen before anyone logs in, and the lock screen |

The system's are in `/system/wallpapers` (`SystemPaths.WallpapersFolder`). The kernel carries
its own (`Resources/Wallpapers`, drawn by `tools/make_wallpapers.py`: layered hills under a
dusk sky for the desktop, under a moonlit night for the lock screen) and `DiskLayout` writes
each role's at boot when the folder has no file for it in either format, so an image an
administrator replaced, even with a `.bmp`, stays.

Each account has `/users/<name>/config/wallpapers` (`Account.WallpapersFolder`), made with
its profile: a `desktop` or `lock` image there replaces the system's for the account, as its
cursors folder does for cursors. For the same reasons there is no setting: one file per role
is all an account chooses.

    cp mountains.bmp /users/<name>/config/wallpapers/desktop.bmp
    config reload

- **Loading** (`Wallpapers.LoadAsync`): the account's own file, read with its session's
  rights, else the system's, else the built-in one. Files are read on the main loop; decoding
  and scaling run on a thread of their own (`BackgroundWork`), so a large image does not stop
  the system. A file that cannot be read or decoded is logged (as the account's entry, for its
  own) and the next one is tried. Meanwhile the colour shows.
- **Which image a screen shows** (`Screen.LoadBackgroundAsync`): a desktop's session shows its
  account's `desktop`. The system's screen, the login window's, shows `lock`: the account's
  whose session was just locked at the machine (`LogonSessions.LockedAtMachine`, as Windows
  shows the locked user's lock screen), else the system's. Someone logging in at the machine,
  or the locked session ending, gives the system's back (`Screen.ChooseLockScreen`, on
  `LogonSessions.MachineChanged`).
- **Changes** show from the next login, or at once after `config reload`
  (`Screen.ReloadPersonalization`, which also reloads the login screen when it shows the
  account's lock screen). Replaced system images show on screens that load their background
  afterwards.
- With `login.screen = text` there is no lock screen image: locking brings back the
  console's login prompt.

## Order

1. The BMP and cursor readers, the cursor as an image file sent through the scene, the
   Bibata set (done).
2. The screen showing the set's cursors for what is under the pointer (done).
3. Each account's own cursors folder (done).
4. QOI, scaling, the background in the `Scene` and both renderers (done).
5. The wallpapers and lock screen, the system's and each account's own (done).

## Not now

- "Set as wallpaper" in Explorer. The image viewer (`imageview`) has it: its "Wallpaper" and
  "Lock screen" buttons copy the image into the account's wallpapers folder (removing one of
  the other format, which would come first) and reload its screens.
- Other ways to fit an image (whole with bars, centred, tiled) and a background colour of
  one's own: every image fills the screen, the colour is fixed.
- Sessions sharing one decoded copy of the same background.
- PNG and JPEG: they need inflate and a DCT decoder; convert to BMP or QOI on the host.
- Animated cursors (`.ani`) and cursors holding a PNG.
