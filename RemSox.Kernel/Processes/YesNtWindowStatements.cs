using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.GUI;
using RemSox.Kernel.UI.GUI.UIEelements;
using RemSox.Kernel.UI.GUI.UIEelements.Controls;
using RemSox.Kernel.UI.GUI.UIEelements.Shapes;
using RemSox.Kernel.UI.GUI.Windows;

using System.Diagnostics.CodeAnalysis;
using System.Drawing;

using YesNt.Interpreter.Runtime;
using YesNt.Interpreter.Utilities;

using ShapesRectangle = RemSox.Kernel.UI.GUI.UIEelements.Shapes.Rectangle;

namespace RemSox.Kernel.Processes;

/// <summary>
/// <para>
/// Registers all YesNt statements that expose the window and UI element API.
/// </para>
/// <para>
/// All ui_* and win_* statements support two argument styles:
/// <br/>  positional:  ui_rect ${id} 20 20 100 60 0 128 255
/// <br/>  named:       ui_rect ${id} x=20 y=20 w=100 h=60 r=0 g=128 b=255
/// </para>
/// <para>
/// Commas are treated as whitespace in both modes, so you can write:
/// <br/>  ui_rect ${id} x=20,y=20,w=100,h=60,r=0,g=128,b=255
/// </para>
/// <para>
/// If any argument contains '=', the statement parses in named mode
/// (order-independent). Otherwise, positional mode is used.
/// </para>
/// <para>
/// Substitutions:
/// <br/>  %win_last_id   expands to the id of the last created window
/// <br/>  %ui_last_id    expands to the id of the last created UI element
/// <br/>  %ui_clicked id expands to True once for each click on a button (or Enter in a text
///                    box) since it was last asked, else False
/// <br/>  %ui_value id   expands to an element's value: True/False for a check box or radio
///                    button, the number of a slider or progress bar, the text of a text box,
///                    label or button
/// </para>
/// <para>
/// <br/>  ui_set id value  sets that value (the rest of the line), so a script changes a
///                      label's text or a bar's value without drawing it again
/// <br/>  ui_wait [ms]     (registered by <see cref="YesNtInterpreterProcess"/>) waits for
///                      <see cref="EventCount"/> to change: a click, a change, an Enter
/// </para>
/// </summary>
public sealed class YesNtWindowStatements(Process ownerProcess)
{
    private const int DefaultFontSize = 16;
    private const string ClickedToken = "%ui_clicked";
    private const string ValueToken = "%ui_value";

    private readonly Dictionary<int, Window> windows = [];
    private readonly Dictionary<int, object> uiElements = [];

    // Clicks on each button (and Enters in each text box) not yet asked about with %ui_clicked.
    private readonly Dictionary<int, int> clicks = [];

    private int lastWindowId;
    private int lastUiId;

    /// <summary>
    /// Gets how many events the user caused in the script's windows: clicks, changes of a check
    /// box, radio button or slider, and Enter in a text box. <c>ui_wait</c> waits for it to change.
    /// </summary>
    public int EventCount { get; private set; }

    public void Register(YesNtInterpreter interpreter)
    {
        RegisterWindowStatements(interpreter);
        RegisterButtonStatement(interpreter);
        RegisterCheckBoxStatement(interpreter);
        RegisterLabelStatement(interpreter);
        RegisterTextBoxStatement(interpreter);
        RegisterRadioButtonStatement(interpreter);
        RegisterProgressBarStatement(interpreter);
        RegisterSliderStatement(interpreter);
        RegisterPanelStatement(interpreter);
        RegisterLineStatement(interpreter);
        RegisterRectStatement(interpreter);
        RegisterCircleStatement(interpreter);
        RegisterRemoveStatement(interpreter);
        RegisterLastIdSubstitutions(interpreter);
        RegisterEventStatements(interpreter);
    }

    // --- Argument parser ---

    private static string[] ParseTokens(string input)
    {
        return input.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsNamed(string[] tokens)
    {
        foreach (string t in tokens)
        {
            if (t.Contains('='))
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryGetInt(string[] tokens, bool named, string name, int pos, out int value)
    {
        value = 0;

        if (named)
        {
            string prefix = name + '=';
            foreach (string t in tokens)
            {
                if (t.StartsWith(prefix) && int.TryParse(t.AsSpan(prefix.Length), out value))
                {
                    return true;
                }
            }
            return false;
        }

        if (pos < tokens.Length && int.TryParse(tokens[pos], out value))
        {
            return true;
        }
        return false;
    }

    private static bool TryGetString(string[] tokens, bool named, string name, int pos, out string value)
    {
        value = string.Empty;

        if (named)
        {
            string prefix = name + '=';
            foreach (string t in tokens)
            {
                if (t.StartsWith(prefix))
                {
                    value = t[prefix.Length..].FromSafeString();
                    return true;
                }
            }
            return false;
        }

        if (pos < tokens.Length)
        {
            value = tokens[pos].FromSafeString();
            return true;
        }
        return false;
    }

    // --- Window statements ---

    private void RegisterWindowStatements(YesNtInterpreter interpreter)
    {
        // win_create "Title" w h [chrome=true|false]
        interpreter.AddStatement(
            new StatementInformation("win_create", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                string trimmed = args.Trim();
                string title;
                int w, h;
                bool chrome = true;

                if (trimmed.StartsWith('"'))
                {
                    int end = trimmed.IndexOf('"', 1);
                    if (end < 0)
                    {
                        context.Exit("[win_create] Missing closing quote", false);
                        return;
                    }
                    title = trimmed[1..end];
                    string[] tokens = ParseTokens(trimmed[(end + 1)..]);
                    bool named = IsNamed(tokens);

                    if (!TryGetInt(tokens, named, "w", 0, out w) || !TryGetInt(tokens, named, "h", 1, out h))
                    {
                        context.Exit("[win_create] Expected width and height", false);
                        return;
                    }

                    if (TryGetString(tokens, named, "chrome", 2, out string chromeStr))
                    {
                        _ = bool.TryParse(chromeStr, out chrome);
                    }
                }
                else
                {
                    string[] tokens = ParseTokens(trimmed);
                    bool named = IsNamed(tokens);

                    if (!TryGetString(tokens, named, "title", 0, out title) ||
                        !TryGetInt(tokens, named, "w", 1, out w) ||
                        !TryGetInt(tokens, named, "h", 2, out h))
                    {
                        context.Exit("[win_create] Usage: win_create \"Title\" w h [chrome=false]", false);
                        return;
                    }

                    if (TryGetString(tokens, named, "chrome", 3, out string chromeStr))
                    {
                        _ = bool.TryParse(chromeStr, out chrome);
                    }
                }

                Window win = ownerProcess.Screen.CreateWindow(ownerProcess, title, new Size(w, h));
                win.HasChrome = chrome;

                int id = lastWindowId = win.Id;
                windows[id] = win;
            });

        // win_close id
        interpreter.AddStatement(
            new StatementInformation("win_close", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                if (TryGetWindow(args, out Window? win))
                {
                    win.Screen.CloseWindow(win);
                    _ = windows.Remove(win.Id);
                }
            });

        // win_flush id
        interpreter.AddStatement(
            new StatementInformation("win_flush", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                if (TryGetWindow(args, out Window? win))
                {
                    win.Flush();
                }
            });

        // win_title id "New Title"
        interpreter.AddStatement(
            new StatementInformation("win_title", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                string trimmed = args.Trim();
                string[] tokens = ParseTokens(trimmed);
                bool named = IsNamed(tokens);

                if (!TryGetInt(tokens, named, "id", 0, out int id) || !windows.TryGetValue(id, out Window? win))
                {
                    return;
                }

                string title;
                if (trimmed.Contains('"'))
                {
                    int start = trimmed.IndexOf('"') + 1;
                    int end = trimmed.IndexOf('"', start);
                    title = end >= 0 ? trimmed[start..end] : trimmed[start..];
                }
                else
                {
                    _ = TryGetString(tokens, named, "title", 1, out title);
                }

                win.Title = title;
            });

        // win_autoflush id true|false
        interpreter.AddStatement(
            new StatementInformation("win_autoflush", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                string[] tokens = ParseTokens(args.Trim());
                bool named = IsNamed(tokens);

                if (TryGetInt(tokens, named, "id", 0, out int id) &&
                    TryGetString(tokens, named, "enabled", 1, out string enabledStr) &&
                    bool.TryParse(enabledStr, out bool enabled) &&
                    windows.TryGetValue(id, out Window? win))
                {
                    win.AutoFlush = enabled;
                }
            });

        // win_chrome id true|false
        interpreter.AddStatement(
            new StatementInformation("win_chrome", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                string[] tokens = ParseTokens(args.Trim());
                bool named = IsNamed(tokens);

                if (TryGetInt(tokens, named, "id", 0, out int id) &&
                    TryGetString(tokens, named, "chrome", 1, out string chromeStr) &&
                    bool.TryParse(chromeStr, out bool chrome) &&
                    windows.TryGetValue(id, out Window? win))
                {
                    win.HasChrome = chrome;
                }
            });

        // win_focus id
        interpreter.AddStatement(
            new StatementInformation("win_focus", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                if (TryGetWindow(args, out Window? win))
                {
                    win.Screen.FocusWindow(win);
                }
            });

        // win_move id x y
        interpreter.AddStatement(
            new StatementInformation("win_move", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                string[] tokens = ParseTokens(args.Trim());
                bool named = IsNamed(tokens);

                if (TryGetInt(tokens, named, "id", 0, out int id) &&
                    TryGetInt(tokens, named, "x", 1, out int x) &&
                    TryGetInt(tokens, named, "y", 2, out int y) &&
                    windows.TryGetValue(id, out Window? win))
                {
                    win.Position = new Point(x, y);
                }
            });

        // win_resize id w h
        interpreter.AddStatement(
            new StatementInformation("win_resize", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                string[] tokens = ParseTokens(args.Trim());
                bool named = IsNamed(tokens);

                if (TryGetInt(tokens, named, "id", 0, out int id) &&
                    TryGetInt(tokens, named, "w", 1, out int w) &&
                    TryGetInt(tokens, named, "h", 2, out int h) &&
                    windows.TryGetValue(id, out Window? win))
                {
                    win.Size = new Size(w, h);
                }
            });

        // win_invalidate_all
        interpreter.AddStatement(
            new StatementInformation("win_invalidate_all", YesNt.Interpreter.Enums.SearchMode.Contains, YesNt.Interpreter.Enums.SpaceAround.None)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) => ownerProcess.Screen.InvalidateAll());
    }

    // --- Control statements ---

    private void RegisterButtonStatement(YesNtInterpreter interpreter)
    {
        interpreter.AddStatement(
            new StatementInformation("ui_button", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                if (!TryGetWindowAndArgs(args, out Window? win, out string[] tokens, out bool named))
                {
                    return;
                }

                if (!TryGetInt(tokens, named, "x", 0, out int x) ||
                    !TryGetInt(tokens, named, "y", 1, out int y) ||
                    !TryGetInt(tokens, named, "w", 2, out int w) ||
                    !TryGetInt(tokens, named, "h", 3, out int h) ||
                    !TryGetString(tokens, named, "label", 4, out string label))
                {
                    context.Exit("[ui_button] Usage: ui_button winId x y w h \"Label\" [r g b]", false);
                    return;
                }

                Color color = ParseColor(tokens, named, 5);

                Button button = win.CreateUIElement<Button>(b =>
                {
                    b.Position = new Point(x, y);
                    b.Size = new Size(w, h);
                    b.Text = label;
                    if (color != Color.Empty)
                    {
                        b.BackgroundColor = color;
                    }
                });

                lastUiId = button.Id;
                uiElements[lastUiId] = button;
                int buttonId = button.Id;
                button.OnClick += (_, _) => Clicked(buttonId);
            });
    }

    private void RegisterCheckBoxStatement(YesNtInterpreter interpreter)
    {
        interpreter.AddStatement(
            new StatementInformation("ui_checkbox", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                if (!TryGetWindowAndArgs(args, out Window? win, out string[] tokens, out bool named))
                {
                    return;
                }

                if (!TryGetInt(tokens, named, "x", 0, out int x) ||
                    !TryGetInt(tokens, named, "y", 1, out int y) ||
                    !TryGetString(tokens, named, "label", 2, out string label))
                {
                    context.Exit("[ui_checkbox] Usage: ui_checkbox winId x y \"Label\" [checked] [r g b]", false);
                    return;
                }

                int checkPos = named ? -1 : 3;
                _ = TryGetString(tokens, named, "checked", checkPos, out string checkedStr);
                _ = bool.TryParse(checkedStr, out bool isChecked);

                Color color = ParseColor(tokens, named, named ? -1 : 4);

                CheckBox checkBox = win.CreateUIElement<CheckBox>(c =>
                {
                    c.Position = new Point(x, y);
                    c.Text = label;
                    c.IsChecked = isChecked;
                    if (color != Color.Empty)
                    {
                        c.BackgroundColor = color;
                    }
                });

                lastUiId = checkBox.Id;
                uiElements[lastUiId] = checkBox;
                checkBox.OnCheckedChanged += (_, _) => EventCount++;
            });
    }

    private void RegisterLabelStatement(YesNtInterpreter interpreter)
    {
        interpreter.AddStatement(
            new StatementInformation("ui_label", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                if (!TryGetWindowAndArgs(args, out Window? win, out string[] tokens, out bool named))
                {
                    return;
                }

                if (!TryGetInt(tokens, named, "x", 0, out int x) ||
                    !TryGetInt(tokens, named, "y", 1, out int y) ||
                    !TryGetString(tokens, named, "label", 2, out string text))
                {
                    context.Exit("[ui_label] Usage: ui_label winId x y \"Text\" [fontSize] [r g b]", false);
                    return;
                }

                if (!TryGetInt(tokens, named, "fontSize", 3, out int fontSize) || fontSize <= 0)
                {
                    fontSize = DefaultFontSize;
                }

                ThemeColor color = Foreground(ParseColor(tokens, named, named ? -1 : 4));

                Text label = win.CreateUIElement<Text>(t =>
                {
                    t.Position = new Point(x, y);
                    t.Content = text;
                    t.FontSize = fontSize;
                    t.Color = color;
                });

                lastUiId = label.Id;
                uiElements[lastUiId] = label;
            });
    }

    private void RegisterTextBoxStatement(YesNtInterpreter interpreter)
    {
        // ui_textbox winId x y w h ["Text"]: a box the user types in; Enter counts as a click.
        interpreter.AddStatement(
            new StatementInformation("ui_textbox", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                if (!TryGetWindowAndArgs(args, out Window? win, out string[] tokens, out bool named))
                {
                    return;
                }

                if (!TryGetInt(tokens, named, "x", 0, out int x) ||
                    !TryGetInt(tokens, named, "y", 1, out int y) ||
                    !TryGetInt(tokens, named, "w", 2, out int w) ||
                    !TryGetInt(tokens, named, "h", 3, out int h))
                {
                    context.Exit("[ui_textbox] Usage: ui_textbox winId x y w h [\"Text\"]", false);
                    return;
                }

                _ = TryGetString(tokens, named, "text", 4, out string text);

                TextBox box = win.CreateUIElement<TextBox>(b =>
                {
                    b.Position = new Point(x, y);
                    b.Size = new Size(w, h);
                    b.Text = text;
                });

                lastUiId = box.Id;
                uiElements[lastUiId] = box;
                int boxId = box.Id;
                box.Submitted += _ => Clicked(boxId);
            });
    }

    private void RegisterRadioButtonStatement(YesNtInterpreter interpreter)
    {
        interpreter.AddStatement(
            new StatementInformation("ui_radio", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                if (!TryGetWindowAndArgs(args, out Window? win, out string[] tokens, out bool named))
                {
                    return;
                }

                if (!TryGetInt(tokens, named, "x", 0, out int x) ||
                    !TryGetInt(tokens, named, "y", 1, out int y) ||
                    !TryGetString(tokens, named, "label", 2, out string label))
                {
                    context.Exit("[ui_radio] Usage: ui_radio winId x y \"Label\" [checked] [r g b]", false);
                    return;
                }

                _ = TryGetString(tokens, named, "checked", 3, out string checkedStr);
                _ = bool.TryParse(checkedStr, out bool isChecked);

                Color color = ParseColor(tokens, named, 4);

                RadioButton radio = win.CreateUIElement<RadioButton>(r =>
                {
                    r.Position = new Point(x, y);
                    r.Text = label;
                    r.IsChecked = isChecked;
                    if (color != Color.Empty)
                    {
                        r.BackgroundColor = color;
                    }
                });

                lastUiId = radio.Id;
                uiElements[lastUiId] = radio;
                radio.OnCheckedChanged += (_, _) => EventCount++;
            });
    }

    private void RegisterProgressBarStatement(YesNtInterpreter interpreter)
    {
        interpreter.AddStatement(
            new StatementInformation("ui_progressbar", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                if (!TryGetWindowAndArgs(args, out Window? win, out string[] tokens, out bool named))
                {
                    return;
                }

                if (!TryGetInt(tokens, named, "x", 0, out int x) ||
                    !TryGetInt(tokens, named, "y", 1, out int y) ||
                    !TryGetInt(tokens, named, "w", 2, out int w) ||
                    !TryGetInt(tokens, named, "h", 3, out int h))
                {
                    context.Exit("[ui_progressbar] Usage: ui_progressbar winId x y w h [value] [r g b]", false);
                    return;
                }

                _ = TryGetInt(tokens, named, "value", 4, out int value);

                ProgressBar bar = win.CreateUIElement<ProgressBar>(p =>
                {
                    p.Position = new Point(x, y);
                    p.Size = new Size(w, h);
                    p.Value = value;
                });

                Color fillColor = ParseColor(tokens, named, 5);
                if (fillColor != Color.Empty)
                {
                    bar.FillColor = fillColor;
                }

                lastUiId = bar.Id;
                uiElements[lastUiId] = bar;
            });
    }

    private void RegisterSliderStatement(YesNtInterpreter interpreter)
    {
        interpreter.AddStatement(
            new StatementInformation("ui_slider", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                if (!TryGetWindowAndArgs(args, out Window? win, out string[] tokens, out bool named))
                {
                    return;
                }

                if (!TryGetInt(tokens, named, "x", 0, out int x) ||
                    !TryGetInt(tokens, named, "y", 1, out int y) ||
                    !TryGetInt(tokens, named, "w", 2, out int w) ||
                    !TryGetInt(tokens, named, "h", 3, out int h))
                {
                    context.Exit("[ui_slider] Usage: ui_slider winId x y w h [min] [max] [value]", false);
                    return;
                }

                Slider slider = win.CreateUIElement<Slider>(s =>
                {
                    s.Position = new Point(x, y);
                    s.Size = new Size(w, h);

                    if (TryGetInt(tokens, named, "min", 4, out int min))
                    {
                        s.MinValue = min;
                    }
                    if (TryGetInt(tokens, named, "max", 5, out int max))
                    {
                        s.MaxValue = max;
                    }
                    if (TryGetInt(tokens, named, "value", 6, out int value))
                    {
                        s.Value = value;
                    }
                });

                lastUiId = slider.Id;
                uiElements[lastUiId] = slider;
                slider.OnValueChanged += (_, _) => EventCount++;
            });
    }

    private void RegisterPanelStatement(YesNtInterpreter interpreter)
    {
        interpreter.AddStatement(
            new StatementInformation("ui_panel", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                if (!TryGetWindowAndArgs(args, out Window? win, out string[] tokens, out bool named))
                {
                    return;
                }

                if (!TryGetInt(tokens, named, "x", 0, out int x) ||
                    !TryGetInt(tokens, named, "y", 1, out int y) ||
                    !TryGetInt(tokens, named, "w", 2, out int w) ||
                    !TryGetInt(tokens, named, "h", 3, out int h))
                {
                    context.Exit("[ui_panel] Usage: ui_panel winId x y w h [r g b]", false);
                    return;
                }

                Panel panel = win.CreateUIElement<Panel>(p =>
                {
                    p.Position = new Point(x, y);
                    p.Size = new Size(w, h);
                });

                Color color = ParseColor(tokens, named, 4);
                if (color != Color.Empty)
                {
                    panel.BackgroundColor = color;
                }

                lastUiId = panel.Id;
                uiElements[lastUiId] = panel;
            });
    }

    // --- Shape statements ---

    private void RegisterLineStatement(YesNtInterpreter interpreter)
    {
        interpreter.AddStatement(
            new StatementInformation("ui_line", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                if (!TryGetWindowAndArgs(args, out Window? win, out string[] tokens, out bool named))
                {
                    return;
                }

                if (!TryGetInt(tokens, named, "x1", 0, out int x1) ||
                    !TryGetInt(tokens, named, "y1", 1, out int y1) ||
                    !TryGetInt(tokens, named, "x2", 2, out int x2) ||
                    !TryGetInt(tokens, named, "y2", 3, out int y2))
                {
                    context.Exit("[ui_line] Usage: ui_line winId x1 y1 x2 y2 r g b", false);
                    return;
                }

                ThemeColor color = Foreground(ParseColor(tokens, named, 4));

                Line line = win.CreateUIElement<Line>(l =>
                {
                    l.Position = new Point(x1, y1);
                    l.EndPosition = new Point(x2, y2);
                    l.Color = color;
                });

                lastUiId = line.Id;
                uiElements[lastUiId] = line;
            });
    }

    private void RegisterRectStatement(YesNtInterpreter interpreter)
    {
        interpreter.AddStatement(
            new StatementInformation("ui_rect", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                if (!TryGetWindowAndArgs(args, out Window? win, out string[] tokens, out bool named))
                {
                    return;
                }

                if (!TryGetInt(tokens, named, "x", 0, out int x) ||
                    !TryGetInt(tokens, named, "y", 1, out int y) ||
                    !TryGetInt(tokens, named, "w", 2, out int w) ||
                    !TryGetInt(tokens, named, "h", 3, out int h))
                {
                    context.Exit("[ui_rect] Usage: ui_rect winId x y w h r g b", false);
                    return;
                }

                ThemeColor color = Foreground(ParseColor(tokens, named, 4));

                ShapesRectangle rect = win.CreateUIElement<ShapesRectangle>(r =>
                {
                    r.Position = new Point(x, y);
                    r.Size = new Size(w, h);
                    r.Color = color;
                });

                lastUiId = rect.Id;
                uiElements[lastUiId] = rect;
            });
    }

    private void RegisterCircleStatement(YesNtInterpreter interpreter)
    {
        interpreter.AddStatement(
            new StatementInformation("ui_circle", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                if (!TryGetWindowAndArgs(args, out Window? win, out string[] tokens, out bool named))
                {
                    return;
                }

                if (!TryGetInt(tokens, named, "x", 0, out int x) ||
                    !TryGetInt(tokens, named, "y", 1, out int y) ||
                    !TryGetInt(tokens, named, "radius", 2, out int radius))
                {
                    context.Exit("[ui_circle] Usage: ui_circle winId x y radius r g b [filled=false]", false);
                    return;
                }

                ThemeColor color = Foreground(ParseColor(tokens, named, 3));

                _ = TryGetString(tokens, named, "filled", 6, out string filledStr);
                _ = bool.TryParse(filledStr, out bool filled);

                Circle circle = win.CreateUIElement<Circle>(c =>
                {
                    c.Position = new Point(x, y);
                    c.Radius = radius;
                    c.Color = color;
                    c.IsFilled = filled;
                });

                lastUiId = circle.Id;
                uiElements[lastUiId] = circle;
            });
    }

    // --- Element management ---

    private void RegisterRemoveStatement(YesNtInterpreter interpreter)
    {
        interpreter.AddStatement(
            new StatementInformation("ui_remove", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                string[] tokens = ParseTokens(args.Trim());
                bool named = IsNamed(tokens);

                if (!TryGetInt(tokens, named, "id", 0, out int id))
                {
                    return;
                }

                if (uiElements.TryGetValue(id, out object? elem))
                {
                    if (elem is UIElement uiElem)
                    {
                        // Find the window that owns this element
                        foreach (KeyValuePair<int, Window> kvp in windows)
                        {
                            kvp.Value.RemoveUIElement(uiElem.Id);
                        }
                    }
                    _ = uiElements.Remove(id);
                    _ = clicks.Remove(id);
                }
            });
    }

    // --- Substitutions ---

    private void RegisterLastIdSubstitutions(YesNtInterpreter interpreter)
    {
        interpreter.AddStatement(
            new StatementInformation("%win_last_id", YesNt.Interpreter.Enums.SearchMode.Contains, YesNt.Interpreter.Enums.SpaceAround.None)
            { KeepStatementInArgs = true, Priority = YesNt.Interpreter.Enums.Priority.PreProcessing },
            (args, context) =>
            {
                context.CurrentLine = TemplateProcessor.ProcessSimplePlaceholders(args, "%win_last_id", lastWindowId.ToString());
            });

        interpreter.AddStatement(
            new StatementInformation("%ui_last_id", YesNt.Interpreter.Enums.SearchMode.Contains, YesNt.Interpreter.Enums.SpaceAround.None)
            { KeepStatementInArgs = true, Priority = YesNt.Interpreter.Enums.Priority.PreProcessing },
            (args, context) =>
            {
                context.CurrentLine = TemplateProcessor.ProcessSimplePlaceholders(args, "%ui_last_id", lastUiId.ToString());
            });
    }

    // --- Events and values ---

    private void RegisterEventStatements(YesNtInterpreter interpreter)
    {
        // After ${...} (Highest), so an id may come from a variable.
        interpreter.AddStatement(
            new StatementInformation(ClickedToken, YesNt.Interpreter.Enums.SearchMode.Contains, YesNt.Interpreter.Enums.SpaceAround.None)
            { KeepStatementInArgs = true, Priority = YesNt.Interpreter.Enums.Priority.VeryHigh },
            (args, context) => context.CurrentLine = ReplaceIdTokens(args, ClickedToken, id => TakeClick(id) ? "True" : "False"));

        interpreter.AddStatement(
            new StatementInformation(ValueToken, YesNt.Interpreter.Enums.SearchMode.Contains, YesNt.Interpreter.Enums.SpaceAround.None)
            { KeepStatementInArgs = true, Priority = YesNt.Interpreter.Enums.Priority.VeryHigh },
            (args, context) => context.CurrentLine = ReplaceIdTokens(args, ValueToken, id => StringExtensions.ToSafeString(GetValue(id))));

        // ui_set id value
        interpreter.AddStatement(
            new StatementInformation("ui_set", YesNt.Interpreter.Enums.SearchMode.StartOfLine, YesNt.Interpreter.Enums.SpaceAround.End)
            { Priority = YesNt.Interpreter.Enums.Priority.Normal },
            (args, context) =>
            {
                string trimmed = args.Trim();
                int space = trimmed.IndexOf(' ');
                string idText = space < 0 ? trimmed : trimmed[..space];
                string value = space < 0 ? string.Empty : Unquote(trimmed[(space + 1)..].Trim().FromSafeString());

                if (!int.TryParse(idText, out int id) || !uiElements.TryGetValue(id, out object? element))
                {
                    context.Exit($"[ui_set] No element {idText}", false);
                    return;
                }

                if (!SetValue(element, value))
                {
                    context.Exit($"[ui_set] Cannot set {element.GetType().Name} {id} to {value}", false);
                }
            });
    }

    private void Clicked(int id)
    {
        clicks[id] = clicks.GetValueOrDefault(id) + 1;
        EventCount++;
    }

    private bool TakeClick(int id)
    {
        if (clicks.GetValueOrDefault(id) <= 0)
        {
            return false;
        }

        clicks[id]--;
        return true;
    }

    // Replaces each "<token> <id>" in the line with what the id gives.
    private static string ReplaceIdTokens(string line, string token, Func<int, string> value)
    {
        int at = line.IndexOf(token, StringComparison.Ordinal);
        while (at >= 0)
        {
            int idStart = at + token.Length;
            while (idStart < line.Length && line[idStart] == ' ')
            {
                idStart++;
            }

            int idEnd = idStart;
            while (idEnd < line.Length && char.IsAsciiDigit(line[idEnd]))
            {
                idEnd++;
            }

            string replacement = int.TryParse(line.AsSpan(idStart, idEnd - idStart), out int id) ? value(id) : string.Empty;
            line = string.Concat(line.AsSpan(0, at), replacement, line.AsSpan(idEnd));
            at = line.IndexOf(token, at + replacement.Length, StringComparison.Ordinal);
        }

        return line;
    }

    private string GetValue(int id)
    {
        return uiElements.GetValueOrDefault(id) switch
        {
            CheckBox checkBox => checkBox.IsChecked ? "True" : "False",
            RadioButton radio => radio.IsChecked ? "True" : "False",
            Slider slider => slider.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ProgressBar bar => bar.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TextBox box => box.Text,
            Text text => text.Content,
            Button button => button.Text,
            _ => string.Empty,
        };
    }

    private static bool SetValue(object element, string value)
    {
        switch (element)
        {
            case CheckBox checkBox when bool.TryParse(value, out bool isChecked):
                checkBox.IsChecked = isChecked;
                return true;
            case RadioButton radio when bool.TryParse(value, out bool isChecked):
                radio.IsChecked = isChecked;
                return true;
            case Slider slider when int.TryParse(value, out int number):
                slider.Value = number;
                return true;
            case ProgressBar bar when int.TryParse(value, out int number):
                bar.Value = number;
                return true;
            case TextBox box:
                box.Text = value;
                return true;
            case Text text:
                text.Content = value;
                return true;
            case Button button:
                button.Text = value;
                return true;
            default:
                return false;
        }
    }

    // A value written in quotes (to keep its spaces or keywords) is set without them.
    private static string Unquote(string value)
    {
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
    }

    // --- Helpers ---

    private bool TryGetWindow(string input, [NotNullWhen(true)] out Window? win)
    {
        win = null;
        string[] tokens = ParseTokens(input.Trim());
        bool named = IsNamed(tokens);

        if (TryGetInt(tokens, named, "id", 0, out int id) && windows.TryGetValue(id, out win))
        {
            return true;
        }
        return false;
    }

    private bool TryGetWindowAndArgs(string input, [NotNullWhen(true)] out Window? win, out string[] tokens, out bool named)
    {
        win = null;
        tokens = ParseTokens(input.Trim());
        named = IsNamed(tokens);

        if (!TryGetInt(tokens, named, "winId", 0, out int winId) || !windows.TryGetValue(winId, out win))
        {
            return false;
        }

        // Positional arguments count from the one after the window's id.
        if (!named)
        {
            tokens = tokens[1..];
        }

        return true;
    }

    // The colour a script gave, or the theme's text colour, which stands out on the window's
    // background in every theme.
    private static ThemeColor Foreground(Color color)
    {
        return color == Color.Empty ? ThemeRole.Text : color;
    }

    private static Color ParseColor(string[] tokens, bool named, int pos)
    {
        if (TryGetInt(tokens, named, "r", pos, out int r) &&
            TryGetInt(tokens, named, "g", pos + 1, out int g) &&
            TryGetInt(tokens, named, "b", pos + 2, out int b))
        {
            return Color.FromArgb(r, g, b);
        }
        return Color.Empty;
    }
}
