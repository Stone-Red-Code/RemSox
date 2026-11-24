# Theming: a modern look, chosen per account

Status: the theme, the drawing and hover states are implemented (order steps 1 to 3);
rounded windows and the new look are proposed.
Builds on `personalization.md` (per screen, so per session; the scene
carries it) and `settings.md` (account settings over the machine's).

The GUI looks like the 90s: square windows with an 18 pixel title bar and tiny buttons, light
grey buttons with a dark grey frame on dark windows, a black taskbar and a start menu of
coloured blocks. Part of that is taste, but most of it is how it is built:

- The drawing commands have square rectangles, lines, circles, text and images, nothing else:
  no rounded corners, and colours go over the wire without alpha, so nothing can be
  translucent.
- Colours are written into the code: about 54 `Color.FromArgb` in 15 files (the window frame,
  each control's default, the desktop, every program). Restyling means editing all of them,
  and nobody can choose anything.

This document gives RemSox a theme: the colours and corner radii everything is drawn with,
read from a file, chosen per account like the wallpaper, and the drawing commands to make it
look current.

## Principles

1. **Nothing names a colour, everything names a role.** Controls, the window frame, the
   desktop and programs ask the theme for "the text colour" or "a selected row", never for
   RGB. A program may still set a colour of its own where the colour is its content (the
   terminal's error lines, a drawing), not its chrome.
2. **Per screen, so per session.** As with the wallpaper, a `Screen` belongs to one logon
   session and draws with its account's theme. The login window's screen draws with the
   machine's.
3. **A theme is a file.** The built-in themes ship as files written to the disk at boot, like
   the cursors, so they can be read, copied and changed. An account may keep its own.
4. **Colours and corners only, not sizes.** Programs lay their windows out with constants
   (`Window.TitleBarHeight`, row heights), so a theme cannot change sizes without breaking
   them. The new sizes are chosen once, in code, for every theme.
5. **No cost per frame.** A theme change re-sends the screen's windows once. Drawing a rounded
   rectangle costs what a square one does; translucency is used only where it is cheap.

## The theme

A `Theme` (`UI/GUI/Theme.cs`) is a set of colour roles and two radii. The roles:

| role | used for |
|------|----------|
| `background` | a window's client area |
| `surface` | the title bar, toolbars, the taskbar, the start menu, panels |
| `field` | inside text boxes, text areas and lists |
| `control` | a button at rest |
| `control.hover` | a button under the pointer |
| `control.pressed` | a button held down |
| `border` | hairlines: control and window frames, separators |
| `border.focus` | the frame of the focused window and field |
| `text` | text |
| `text.muted` | secondary text: status lines, sizes in Explorer, an inactive title |
| `text.disabled` | greyed out: a broken start menu entry |
| `accent` | the one emphasised thing: the start button, a default button, the focus ring, the active task |
| `accent.hover` | an accent button under the pointer |
| `text.on-accent` | text on `accent` |
| `selection` | a selected row, selected text |
| `danger` | the close button under the pointer, Restart and Shut down, error messages |
| `terminal.background`, `terminal.text`, `terminal.error` | the terminal's screen and lines |

and the radii:

| key | default | used for |
|-----|---------|----------|
| `radius` | 4 | controls, fields, list rows, the start menu's entries |
| `window.radius` | 8 | windows and the start menu |

### Built-in themes

Both are defined in code (`Theme.Dark`, `Theme.Light`), the end of every chain of bases.

- `dark` (the default): near-black greys (`background` #202020, `surface` #2B2B2B, `control`
  #3A3A3A), light text, a blue accent (#3478F6), the terminal's light green on near black.
- `light`: white and light greys, dark text, a darker blue accent (#0067C0), a light
  selection with dark text, the terminal dark on white.

## Files and settings

A theme file is `key = value` lines (`KeyValueFile`, the settings' format), colours as
`#RRGGBB` or `#AARRGGBB`:

    # A dark theme with a green accent.
    base = dark
    accent = #3BB273
    accent.hover = #4CC584

`base` names the theme whose values fill in what the file leaves out, so a theme of one's
own can change a single colour. Without one, a file named like a built-in theme is based on
it, any other on `dark`. A base is found like any theme (the account's, the system's, the
built-in one); a file whose base has its own name (an account's `dark` based on `dark`)
continues with the next place, so it changes the system's or the built-in one. A loop is
logged and broken with `dark`.

- **System themes**: `/system/themes/<name>.theme` (`SystemPaths.ThemesFolder`).
  `DiskLayout` writes `dark.theme` and `light.theme` there at boot
  (`Themes.WriteBuiltInFiles`) showing every value as a comment, so they set nothing and the
  built-in values apply. Taking the `#` off a line changes that value for everyone. While a
  file still sets nothing it is rewritten at every boot, so it shows the values of the RemSox
  running; once an administrator changed it, it stays.
- **An account's own**: `/users/<name>/config/themes/<name>.theme` (`Account.ThemesFolder`),
  made with the profile. A theme there is found before a system theme of the same name, so
  an account can change `dark` for itself only.
- **`theme`** (account over machine, default `dark`): the name of the theme to use.
- **`accent`** (account over machine, default empty): a colour that replaces the theme's
  `accent`, with `accent.hover` made lighter from it, so the most common change needs no file
  at all: `config set accent #E0533D`.

An account's files are read through `UserFiles` with the session's caller, as wallpapers
are. A theme that does not exist, a file that cannot be read, or a line that is not a valid
colour, radius or key is logged (as the account's entry, for its own), and the base's value
is used (`dark` for a missing theme). A theme is a few dozen lines, so it is read on the main
loop (`Themes.Load`), when a screen first draws and when it is reloaded.

### Changing it

`config set theme light`, `config set accent ...` and their `config reset` apply at once; a
theme file edited by hand applies after `config reload`. Either way the setting is applied
(`SystemSettings.Apply`), which calls `Screen.ReloadThemes`: every screen that drew already
reads its theme again and, when it looks different (`Theme.LooksLike`), draws all its
windows again. Every screen checks, since a change of the machine's value applies to the
login window and to every account that did not set its own, and it is rare.

## How elements use it

- `Screen.Theme` is the theme its session's account uses, read at its first use; a `Window`
  reads it through its screen, and its elements through their window
  (`UIElement.Theme`, `UIElement.Resolve`) when they are drawn in `ToPrimitives`.
- Colour properties of elements (`BackgroundColor`, `TextColor`, a shape's or `Text`'s
  `Color`, `Window.BackgroundColor`) are a `ThemeColor`: a role, or a fixed colour. Both
  convert to it implicitly (`text.Color = ThemeRole.TextMuted`, `text.Color = Color.Red`), and
  it becomes a colour only when drawn, so a role follows a theme change. Each control's
  default is a role (`Control`'s constructor takes it: a button `control`, a text box
  `field`, a panel `surface`). Programs name roles for their chrome (Explorer's selected row
  is `selection`, a status line `text.muted`, an error `danger`) and keep fixed colours only
  for content (the image viewer's backdrop, YesNt scripts' own colours).
- A theme change has every window of the screen draw again in full (`Window.Invalidate`). It
  happens rarely, so a full re-send is fine.
- `Control.IsHovered` and `Control.IsPressed` (done). Mouse events go to the focused window
  or the one a press is held on, not to the window under the pointer, so the screen tells
  windows where the pointer is: whenever it chooses the pointer's image (after mouse events,
  and every few ticks, since windows open and move under a still pointer) it calls
  `Window.SetPointer` on the window under the pointer, or the one a press is held on, and
  `SetPointer(null)` on the one it left. The window marks the control under the pointer
  hovered; while a press is held only the pressed control can be, and while the window is
  dragged or resized none is. A left press marks its control pressed until the release, and
  it shows pressed only while also hovered, so moving off before releasing shows it at rest,
  as releasing there does nothing. Only controls whose state changed are sent again.
- What shows it: a button's background (`Theme.GetInteractive`: `control.hover` and
  `control.pressed` for `control`, `accent.hover` for `accent`, and for any other colour the
  colour moved a little towards the text, or towards the background when held), the frame
  of check boxes and radio buttons, a scroll bar's thumb, and the title bar buttons
  (`control.hover`, close in `danger`, darker while held).

## Drawing

Two additions to the render commands (`RemSox.Shared`), which raised `RpcHandshake.Version`
to 9 (done), and one still to come:

- **Alpha** (done). `Wire.WriteColor` writes four bytes, ARGB. Fills (`FilledRectangle`,
  `FilledRoundedRectangle`) blend a translucent colour over what their window drew under
  them; outlines, lines, circles, points and text draw their colour opaque in both
  renderers, so they agree without blending every line and letter (`IPrimitiveCanvas`).
  Meant for small washes (hover, selection).
- **Rounded rectangles** (done). `FilledRoundedRectangle` and `RoundedRectangleBorder`, with a
  radius (0-255, at most half the shorter side: `RoundedCorners.Clamp`); the `Rectangle`
  shape draws them when its `CornerRadius` is set. The kernel fills the middle of each row
  as one run (`Array.Fill`) and blends the corner pixels by how much of each the shape
  covers, from a table made once per radius by 4x4 sampling (`RoundedCorners`, shared and
  tested), so corners are smooth. The viewer draws a GDI+ path with antialiasing.
- **Rounded windows** (step 4). `SetWindowStyleCommand` (like `SetBackgroundCommand`, one per screen)
  carries the theme's `window.radius`. When the scene is composited, the pixels of a window's
  canvas outside its rounded corners are skipped, so the wallpaper or the window below shows
  there: a few pixels on the first and last rows, nothing per frame worth measuring. A
  maximized window has square corners.

Measured in the VM, the time to draw a 120x24 button-sized fill on a window's canvas:

| fill | time |
|------|------|
| square, opaque | 0.6 us |
| rounded, radius 4, opaque | 1.1 us |
| rounded, radius 8, opaque | 4.7 us |
| square, translucent | 32 us (11 ns a pixel) |
| rounded, radius 4, translucent | 28 us |
| square outline | 1.0 us |
| rounded outline, radius 4 | 3.7 us |

Rounded corners cost nothing that matters: a window of twenty rounded buttons draws in a
fraction of a millisecond. Translucency costs a blend per pixel, so it stays on small areas;
a translucent full screen would take about 11 ms. When step 4 is done, the time to composite
a frame and to draw a window of controls is measured again, before and after.

## The look

With the theme and the drawing above:

- **Windows**: `window.radius` corners, a one pixel `border` frame (`border.focus` when
  focused), a 30 pixel title bar in `surface` with the title in `text` (`text.muted` when not
  focused), and flat close, maximize and minimize buttons 40 pixels wide that show
  `control.hover` under the pointer, `danger` for close. `Window.TitleBarHeight` becomes 30;
  programs that lay out from it move down with it.
- **Buttons**: `control` with `radius` corners and no frame, `control.hover` and
  `control.pressed`; a default button (`Button.IsDefault`) in `accent`.
- **Text boxes and areas**: `field` with `radius` corners and a `border` frame, `border.focus`
  when focused.
- **Lists** (Explorer's rows): rows with `radius` corners, `selection` when selected and a
  light wash under the pointer.
- **Check boxes, radio buttons, sliders, scroll bars, progress bars**: drawn in `control`,
  `border` and `accent`.
- **Taskbar**: 40 pixels of `surface` with a `border` line on top; the start button in
  `accent`; one button per window, the focused one marked by an `accent` line under it; a
  clock at the right (hours and minutes, updated when the minute changes).
- **Start menu**: a `window.radius` panel in `surface` above the start button, the programs as
  rows (no coloured blocks), a separator, then the account's name with Lock, Log out,
  Restart and Shut down at the bottom.
- **Login window**: the same window and controls, in the machine's theme.

## Order

1. The theme (done): `Theme`, `ThemeColor`, `dark` and `light` with their files, `theme` and
   `accent` applying at once, `Screen.Theme`; every hard-coded chrome colour replaced by a
   role. The shapes stay as they were: only the colours changed (flat grey buttons instead
   of light grey ones, the title bar in `surface` with the focus shown by the frame).
2. Alpha in colours and rounded rectangles, in both renderers (protocol 9) (done; the test
   program shows a translucent bar and a rounded rectangle filled and outlined).
3. Hover and pressed states (done).
4. The new look: window chrome and rounded windows, controls, taskbar with clock, start menu.

Each step is a commit of its own and builds and runs by itself.

## Not now

- **A proportional font.** Text is laid out in fixed cells half as wide as high (DejaVu Sans
  Mono): controls, the terminal and the viewer all rely on it. A proportional font would look
  more current but needs text measuring everywhere; it is a project of its own.
- **Translucent windows and taskbar, drop shadows.** Blending a large area over the wallpaper
  on every frame is what principle 5 rules out; a shadow could be pre-drawn once per window
  size if the measurements allow.
- **Icons** in the start menu, the taskbar and Explorer: they need an icon set (as images,
  like the cursors) and a place to say which program has which.
- **Sizes in themes** (title bar height, font size): only once programs lay out from the
  theme instead of constants.
- **A Settings program** to choose the theme, accent, wallpaper, lock screen and cursors
  without `config`.
