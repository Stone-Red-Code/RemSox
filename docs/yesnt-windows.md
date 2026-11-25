# Windows in YesNt scripts

RemSox gives YesNt scripts (`.ynt`, run with `yesnt <file>` or by opening the file) statements
for windows, controls and shapes. They are RemSox's own, added to the language with the
interpreter's `AddStatement` (`RemSox.Kernel/Processes/YesNtWindowStatements.cs`), and follow
the language's rules:

- **Positional arguments, text last.** Arguments are separated by spaces: first the numbers,
  then any text, which runs to the end of the line, as `list <name> set <index> <value>` does.
  Text may hold spaces and any character (`ui_label ${w} 10 40 x = 1 + 2`); quote it when it
  ends in `:` or holds `"` or keywords (`ui_label ${w} 20 46 "Task:"`). Numbers may be
  fractions, as `calc` gives them, and are rounded.
- **Results through `%out`.** A statement that makes a window or an element gives its id
  through `%out`, and so do `ui_get` and `ui_wait`.
- **What is optional has its own statement.** Colour, font size and filling are set after an
  element is made (`ui_color`, `ui_font_size`, `ui_filled`), not with trailing arguments.
- **Every misuse is an error.** An unknown id, a missing or extra argument, or a value an
  element cannot take ends the script with the statement's name and usage:
  `[ui_set] No element 99. Usage: ui_set <element> [value]`.

Windows and elements share one numbering of the script's own, so an id names one thing, and
`ui_wait` gives either.

```ynt
win_create 300 140 Greeter
var win = %out
win_autoflush ${win} True
ui_textbox ${win} 20 40 260 28
var name = %out
ui_button ${win} 20 84 120 32 Say hello
var hello = %out

label wait:
ui_wait
var fired = %out
if ${fired} == ${hello} call greet
goto wait

func greet:
    ui_get ${name}
    win_title ${win} Hello, %out
end_func
```

## Windows

| statement | does |
|-----------|------|
| `win_create <width> <height> [title]` | Opens a window; `%out` is its id. |
| `win_close <window>` | Closes it. |
| `win_title <window> [title]` | Sets its title. |
| `win_move <window> <x> <y>` | Moves it on the screen. |
| `win_resize <window> <width> <height>` | Resizes it. |
| `win_focus <window>` | Brings it to the front. |
| `win_chrome <window> <True\|False>` | Shows or hides its title bar and frame. |
| `win_autoflush <window> <True\|False>` | Redraws it after every change (off at first). |
| `win_flush <window>` | Redraws it now, when autoflush is off: draw many things, then show them at once. |
| `win_keep_open <window> <True\|False>` | Its close button no longer closes it: `ui_wait` gives the window's id, and the script closes it with `win_close` when it wants to. |
| `win_invalidate_all` | Redraws the whole screen. |

When the user closes a window not kept open, it closes, and the script ends with its last
window, as any program does.

## Controls and shapes

Each gives the new element's id through `%out`.

| statement | makes |
|-----------|-------|
| `ui_button <window> <x> <y> <width> <height> [text]` | A button; a click is its event. |
| `ui_checkbox <window> <x> <y> [text]` | A check box; a change is its event. |
| `ui_radio <window> <x> <y> [text]` | A radio button; a change is its event. |
| `ui_textbox <window> <x> <y> <width> <height> [text]` | A box the user types in; Enter is its event. |
| `ui_slider <window> <x> <y> <width> <height> <min> <max>` | A slider at `<min>`; a change is its event. |
| `ui_progressbar <window> <x> <y> <width> <height>` | A progress bar, from 0 to 100. |
| `ui_label <window> <x> <y> [text]` | Text. |
| `ui_panel <window> <x> <y> <width> <height>` | A plain background. |
| `ui_line <window> <x1> <y1> <x2> <y2>` | A line. |
| `ui_rect <window> <x> <y> <width> <height>` | A filled rectangle. |
| `ui_circle <window> <x> <y> <radius>` | A filled circle. |

Shapes and labels are drawn in the theme's text colour until given another.

## Changing elements

| statement | does |
|-----------|------|
| `ui_set <element> [value]` | Sets the value: `True`/`False` for a check box or radio button, a number for a slider or progress bar, the text of a text box, label or button. |
| `ui_get <element>` | Gives that value through `%out`. |
| `ui_color <element> <red> <green> <blue>` | A shape's or label's colour, a control's background, a progress bar's filling (0 to 255 each). |
| `ui_font_size <element> <size>` | A label's or button's text size. |
| `ui_filled <element> <True\|False>` | Whether a rectangle or circle is filled. |
| `ui_move <element> <x> <y>` | Moves it; a line moves both ends. |
| `ui_remove <element>` | Removes it. |

## Waiting for the user

`ui_wait [milliseconds]` waits until the user clicks a button, changes a check box, radio
button or slider, presses Enter in a text box, or clicks the close button of a window kept
open, and gives that element's or window's id through `%out`. With a time, it gives `0` if
nothing happened by then; without one it waits as long as it takes. Events that happen while
the script is busy are kept, oldest first, each element once, so `ui_wait` returns at once
for them. While it waits the script costs nothing: the main loop idles.

```ynt
ui_wait 250
var fired = %out
if ${fired} == 0 call tick
if ${fired} == ${quit} goto done
```

`examples/timer.ynt` (a focus timer with a text box, a slider and buttons) and
`examples/focus.ynt` (an animation with `sleep` and `ui_move`) show them together.
