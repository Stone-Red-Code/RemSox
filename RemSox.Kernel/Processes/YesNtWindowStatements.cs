using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.GUI;
using RemSox.Kernel.UI.GUI.UIEelements;
using RemSox.Kernel.UI.GUI.UIEelements.Controls;
using RemSox.Kernel.UI.GUI.UIEelements.Shapes;
using RemSox.Kernel.UI.GUI.Windows;

using System.Drawing;
using System.Globalization;

using YesNt.Interpreter.Enums;
using YesNt.Interpreter.Runtime;
using YesNt.Interpreter.Utilities;

using ShapesRectangle = RemSox.Kernel.UI.GUI.UIEelements.Shapes.Rectangle;

namespace RemSox.Kernel.Processes;

/// <summary>
/// The YesNt statements that give a script windows and the controls and shapes in them.
/// </summary>
/// <remarks>
/// <para>
/// They follow the language's own rules (as <c>list &lt;name&gt; set &lt;index&gt; &lt;value&gt;</c>
/// does): arguments are positional and separated by spaces, numbers first and any text last,
/// running to the end of the line, so a label may hold spaces, quotes or <c>=</c>. What is
/// optional (colour, font size, filling) has a statement of its own instead of trailing
/// arguments. A statement that makes something, and <c>ui_get</c> and <c>ui_wait</c>, give
/// their result through <c>%out</c>; every misuse (an unknown id, a missing or extra argument,
/// a value an element cannot take) ends the script with an error rather than being ignored.
/// </para>
/// <para>
/// Windows and elements share one numbering of the script's own, so an id names exactly one
/// thing even across windows (a window numbers its elements on its own), and <c>ui_wait</c>
/// can give either. Statements: <c>win_create w h [title]</c>, <c>win_close</c>,
/// <c>win_flush</c>, <c>win_focus</c>, <c>win_title</c>, <c>win_autoflush</c>,
/// <c>win_chrome</c>, <c>win_move</c>, <c>win_resize</c>, <c>win_keep_open</c>,
/// <c>win_invalidate_all</c>; <c>ui_button</c>, <c>ui_checkbox</c>, <c>ui_radio</c>,
/// <c>ui_label</c>, <c>ui_textbox</c>, <c>ui_progressbar</c>, <c>ui_slider</c>,
/// <c>ui_panel</c>, <c>ui_line</c>, <c>ui_rect</c>, <c>ui_circle</c>; <c>ui_color</c>,
/// <c>ui_font_size</c>, <c>ui_filled</c>, <c>ui_move</c>, <c>ui_set</c>, <c>ui_get</c>,
/// <c>ui_remove</c>, <c>ui_wait</c>. <c>docs/yesnt-windows.md</c> describes each.
/// </para>
/// </remarks>
public sealed class YesNtWindowStatements(Process ownerProcess)
{
    private const int DefaultFontSize = 16;

    // ui_wait's line once it waits: the moment it gives up, so the deadline survives the retries.
    private const char DeadlineMark = '@';

    private readonly Dictionary<int, Window> windows = [];
    private readonly Dictionary<int, UIElement> elements = [];

    // Windows whose close button is an event for the script (win_keep_open), not a close.
    private readonly HashSet<int> keptOpen = [];

    // What the user did and the script has not yet taken with ui_wait: ids, oldest first, each
    // at most once (a dragged slider changes many times while the script looks once).
    private readonly List<int> events = [];

    private int nextId = 1;

    /// <summary>Registers the statements on <paramref name="interpreter"/>.</summary>
    public void Register(YesNtInterpreter interpreter)
    {
        RegisterWindowStatements(interpreter);
        RegisterControlStatements(interpreter);
        RegisterShapeStatements(interpreter);
        RegisterStyleStatements(interpreter);
        RegisterElementStatements(interpreter);
        RegisterWaitStatement(interpreter);
    }

    // --- Window statements ---

    private void RegisterWindowStatements(YesNtInterpreter interpreter)
    {
        Add(interpreter, "win_create", "win_create <width> <height> [title]", 2, (args, text, context) =>
        {
            Window window = ownerProcess.Screen.CreateWindow(ownerProcess, text, new Size(Positive(args[0], "width"), Positive(args[1], "height")));
            int id = nextId++;
            windows[id] = window;
            window.OnCloseRequested += _ => CloseRequested(id);
            context.SetOut(id.ToString(CultureInfo.InvariantCulture));
        });

        Add(interpreter, "win_close", "win_close <window>", 1, NoText((args, _) => CloseWindow(args[0], GetWindow(args[0]))));
        Add(interpreter, "win_flush", "win_flush <window>", 1, NoText((args, _) => GetWindow(args[0]).Flush()));
        Add(interpreter, "win_focus", "win_focus <window>", 1, NoText((args, _) =>
        {
            Window window = GetWindow(args[0]);
            window.Screen.FocusWindow(window);
        }));

        Add(interpreter, "win_title", "win_title <window> [title]", 1, (args, text, _) => GetWindow(args[0]).Title = text);
        Add(interpreter, "win_autoflush", "win_autoflush <window> <True|False>", 1, (args, text, _) => GetWindow(args[0]).AutoFlush = Bool(text));
        Add(interpreter, "win_chrome", "win_chrome <window> <True|False>", 1, (args, text, _) => GetWindow(args[0]).HasChrome = Bool(text));
        Add(interpreter, "win_move", "win_move <window> <x> <y>", 3, NoText((args, _) => GetWindow(args[0]).Position = new Point(args[1], args[2])));
        Add(interpreter, "win_resize", "win_resize <window> <width> <height>", 3, NoText((args, _) =>
            GetWindow(args[0]).Size = new Size(Positive(args[1], "width"), Positive(args[2], "height"))));

        // win_keep_open <window> True: its close button only tells the script (ui_wait gives
        // the window's id), which can ask first and close it with win_close.
        Add(interpreter, "win_keep_open", "win_keep_open <window> <True|False>", 1, (args, text, context) =>
        {
            _ = GetWindow(args[0]);
            if (Bool(text))
            {
                _ = keptOpen.Add(args[0]);
            }
            else
            {
                _ = keptOpen.Remove(args[0]);
            }
        });

        interpreter.AddStatement(
            new StatementInformation("win_invalidate_all", SearchMode.StartOfLine, SpaceAround.None),
            (args, context) =>
            {
                if (args.Trim().Length > 0)
                {
                    context.Exit("[win_invalidate_all] Usage: win_invalidate_all", true);
                    return;
                }

                ownerProcess.Screen.InvalidateAll();
            });
    }

    // --- Control statements ---

    private void RegisterControlStatements(YesNtInterpreter interpreter)
    {
        Add(interpreter, "ui_button", "ui_button <window> <x> <y> <width> <height> [text]", 5, (args, text, context) =>
        {
            Button button = GetWindow(args[0]).CreateUIElement<Button>(b =>
            {
                b.Position = new Point(args[1], args[2]);
                b.Size = new Size(args[3], args[4]);
                b.Text = text;
            });

            int id = AddElement(button, context);
            button.OnClick += (_, _) => Happened(id);
        });

        Add(interpreter, "ui_checkbox", "ui_checkbox <window> <x> <y> [text]", 3, (args, text, context) =>
        {
            CheckBox checkBox = GetWindow(args[0]).CreateUIElement<CheckBox>(c =>
            {
                c.Position = new Point(args[1], args[2]);
                c.Text = text;
            });

            int id = AddElement(checkBox, context);
            checkBox.OnCheckedChanged += (_, _) => Happened(id);
        });

        Add(interpreter, "ui_radio", "ui_radio <window> <x> <y> [text]", 3, (args, text, context) =>
        {
            RadioButton radio = GetWindow(args[0]).CreateUIElement<RadioButton>(r =>
            {
                r.Position = new Point(args[1], args[2]);
                r.Text = text;
            });

            int id = AddElement(radio, context);
            radio.OnCheckedChanged += (_, _) => Happened(id);
        });

        Add(interpreter, "ui_label", "ui_label <window> <x> <y> [text]", 3, (args, text, context) =>
        {
            Text label = GetWindow(args[0]).CreateUIElement<Text>(t =>
            {
                t.Position = new Point(args[1], args[2]);
                t.Content = text;
                t.FontSize = DefaultFontSize;
            });

            _ = AddElement(label, context);
        });

        // A box the user types in; Enter in it is its event.
        Add(interpreter, "ui_textbox", "ui_textbox <window> <x> <y> <width> <height> [text]", 5, (args, text, context) =>
        {
            TextBox box = GetWindow(args[0]).CreateUIElement<TextBox>(b =>
            {
                b.Position = new Point(args[1], args[2]);
                b.Size = new Size(args[3], args[4]);
                b.Text = text;
            });

            int id = AddElement(box, context);
            box.Submitted += _ => Happened(id);
        });

        Add(interpreter, "ui_progressbar", "ui_progressbar <window> <x> <y> <width> <height>", 5, NoText((args, context) =>
        {
            ProgressBar bar = GetWindow(args[0]).CreateUIElement<ProgressBar>(p =>
            {
                p.Position = new Point(args[1], args[2]);
                p.Size = new Size(args[3], args[4]);
            });

            _ = AddElement(bar, context);
        }));

        Add(interpreter, "ui_slider", "ui_slider <window> <x> <y> <width> <height> <min> <max>", 7, NoText((args, context) =>
        {
            if (args[5] >= args[6])
            {
                throw new MisuseException($"The minimum {args[5]} is not below the maximum {args[6]}.");
            }

            Slider slider = GetWindow(args[0]).CreateUIElement<Slider>(s =>
            {
                s.Position = new Point(args[1], args[2]);
                s.Size = new Size(args[3], args[4]);
                s.MinValue = args[5];
                s.MaxValue = args[6];
                s.Value = args[5];
            });

            int id = AddElement(slider, context);
            slider.OnValueChanged += (_, _) => Happened(id);
        }));

        Add(interpreter, "ui_panel", "ui_panel <window> <x> <y> <width> <height>", 5, NoText((args, context) =>
        {
            Panel panel = GetWindow(args[0]).CreateUIElement<Panel>(p =>
            {
                p.Position = new Point(args[1], args[2]);
                p.Size = new Size(args[3], args[4]);
            });

            _ = AddElement(panel, context);
        }));
    }

    // --- Shape statements ---

    private void RegisterShapeStatements(YesNtInterpreter interpreter)
    {
        Add(interpreter, "ui_line", "ui_line <window> <x1> <y1> <x2> <y2>", 5, NoText((args, context) =>
            _ = AddElement(GetWindow(args[0]).CreateUIElement<Line>(l =>
            {
                l.Position = new Point(args[1], args[2]);
                l.EndPosition = new Point(args[3], args[4]);
            }), context)));

        Add(interpreter, "ui_rect", "ui_rect <window> <x> <y> <width> <height>", 5, NoText((args, context) =>
            _ = AddElement(GetWindow(args[0]).CreateUIElement<ShapesRectangle>(r =>
            {
                r.Position = new Point(args[1], args[2]);
                r.Size = new Size(args[3], args[4]);
            }), context)));

        Add(interpreter, "ui_circle", "ui_circle <window> <x> <y> <radius>", 4, NoText((args, context) =>
            _ = AddElement(GetWindow(args[0]).CreateUIElement<Circle>(c =>
            {
                c.Position = new Point(args[1], args[2]);
                c.Radius = Positive(args[3], "radius");
            }), context)));
    }

    // --- Style statements: what is optional, after an element is made ---

    private void RegisterStyleStatements(YesNtInterpreter interpreter)
    {
        // A shape's or label's own colour, a control's background, a progress bar's filling.
        Add(interpreter, "ui_color", "ui_color <element> <red> <green> <blue>", 4, NoText((args, _) =>
        {
            Color color = Color.FromArgb(Channel(args[1]), Channel(args[2]), Channel(args[3]));
            switch (GetElement(args[0]))
            {
                case Shape shape:
                    shape.Color = color;
                    break;
                case Text text:
                    text.Color = color;
                    break;
                case ProgressBar bar:
                    bar.FillColor = color;
                    break;
                case Control control:
                    control.BackgroundColor = color;
                    break;
                default:
                    throw new MisuseException($"Element {args[0]} has no colour.");
            }
        }));

        Add(interpreter, "ui_font_size", "ui_font_size <element> <size>", 2, NoText((args, _) =>
        {
            int size = Positive(args[1], "size");
            switch (GetElement(args[0]))
            {
                case Text text:
                    text.FontSize = size;
                    break;
                case Button button:
                    button.FontSize = size;
                    break;
                default:
                    throw new MisuseException($"Element {args[0]} is not a label or a button.");
            }
        }));

        Add(interpreter, "ui_filled", "ui_filled <element> <True|False>", 1, (args, text, _) =>
        {
            bool filled = Bool(text);
            switch (GetElement(args[0]))
            {
                case Circle circle:
                    circle.IsFilled = filled;
                    break;
                case ShapesRectangle rectangle:
                    rectangle.IsFilled = filled;
                    break;
                default:
                    throw new MisuseException($"Element {args[0]} is not a rectangle or a circle.");
            }
        });
    }

    // --- Elements: move, set, get, remove ---

    private void RegisterElementStatements(YesNtInterpreter interpreter)
    {
        // A line keeps its length and direction: both ends move.
        Add(interpreter, "ui_move", "ui_move <element> <x> <y>", 3, NoText((args, _) =>
        {
            UIElement element = GetElement(args[0]);
            Point to = new(args[1], args[2]);
            if (element is Line line)
            {
                line.EndPosition = new Point(line.EndPosition.X + to.X - line.Position.X, line.EndPosition.Y + to.Y - line.Position.Y);
            }

            element.Position = to;
        }));

        Add(interpreter, "ui_set", "ui_set <element> [value]", 1, (args, text, _) => SetValue(args[0], GetElement(args[0]), text));

        Add(interpreter, "ui_get", "ui_get <element>", 1, NoText((args, context) => context.SetOut(GetValue(args[0], GetElement(args[0])))));

        Add(interpreter, "ui_remove", "ui_remove <element>", 1, NoText((args, context) =>
        {
            UIElement element = GetElement(args[0]);
            element.Window?.RemoveUIElement(element.Id);
            _ = elements.Remove(args[0]);
            _ = events.Remove(args[0]);
        }));
    }

    // ui_wait [milliseconds]: gives through %out the id of the next element the user clicked,
    // changed or pressed Enter in, or of a window kept open whose close button was clicked; 0
    // when the time passed first. Without a time it waits until something happens. Waiting, the
    // script lets the main loop idle (RetryLine asks again each step, without running the line).
    private void RegisterWaitStatement(YesNtInterpreter interpreter)
    {
        interpreter.AddStatement(
            new StatementInformation("ui_wait", SearchMode.StartOfLine, SpaceAround.None),
            (args, context) =>
            {
                string time = args.Trim().FromSafeString();
                if (!TryGetDeadline(time, out long deadline))
                {
                    context.Exit($"[ui_wait] Not a number of milliseconds: {time}. Usage: ui_wait [milliseconds]", true);
                    return;
                }

                if (events.Count > 0)
                {
                    int id = events[0];
                    events.RemoveAt(0);
                    context.SetOut(id.ToString(CultureInfo.InvariantCulture));
                }
                else if (Environment.TickCount64 >= deadline)
                {
                    context.SetOut("0");
                }
                else
                {
                    context.CurrentLine = string.Create(CultureInfo.InvariantCulture, $"ui_wait {DeadlineMark}{deadline}");
                    context.RetryLine(() => events.Count > 0 || Environment.TickCount64 >= deadline);
                }
            });
    }

    // When ui_wait gives up: never with no time, or the moment its line was rewritten to.
    private static bool TryGetDeadline(string time, out long deadline)
    {
        if (time.Length == 0)
        {
            deadline = long.MaxValue;
            return true;
        }

        if (time[0] == DeadlineMark)
        {
            return long.TryParse(time.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out deadline);
        }

        bool valid = int.TryParse(time, NumberStyles.Integer, CultureInfo.InvariantCulture, out int milliseconds) && milliseconds >= 0;
        deadline = Environment.TickCount64 + milliseconds;
        return valid;
    }

    // --- Events ---

    private void Happened(int id)
    {
        if (!events.Contains(id))
        {
            events.Add(id);
        }
    }

    // The close button of one of the script's windows: an event if the script keeps it open,
    // else the window closes, and the script ends with its last window, as any program does.
    private void CloseRequested(int id)
    {
        if (keptOpen.Contains(id))
        {
            Happened(id);
            return;
        }

        if (!windows.TryGetValue(id, out Window? window))
        {
            return;
        }

        CloseWindow(id, window);
        if (windows.Count == 0)
        {
            ProcessManager.StopProcess(window.ProcessId);
        }
    }

    private void CloseWindow(int id, Window window)
    {
        window.Screen.CloseWindow(window);
        _ = windows.Remove(id);
        _ = keptOpen.Remove(id);
        _ = events.Remove(id);

        foreach (int elementId in elements.Where(e => e.Value.Window == window).Select(e => e.Key).ToList())
        {
            _ = elements.Remove(elementId);
            _ = events.Remove(elementId);
        }
    }

    // --- Values ---

    private static string GetValue(int id, UIElement element)
    {
        return element switch
        {
            CheckBox checkBox => checkBox.IsChecked ? "True" : "False",
            RadioButton radio => radio.IsChecked ? "True" : "False",
            Slider slider => slider.Value.ToString(CultureInfo.InvariantCulture),
            ProgressBar bar => bar.Value.ToString(CultureInfo.InvariantCulture),
            TextBox box => box.Text,
            Text text => text.Content,
            Button button => button.Text,
            _ => throw new MisuseException($"Element {id} has no value."),
        };
    }

    private static void SetValue(int id, UIElement element, string value)
    {
        switch (element)
        {
            case CheckBox checkBox:
                checkBox.IsChecked = Bool(value);
                break;
            case RadioButton radio:
                radio.IsChecked = Bool(value);
                break;
            case Slider slider:
                slider.Value = Number(value, "value");
                break;
            case ProgressBar bar:
                bar.Value = Number(value, "value");
                break;
            case TextBox box:
                box.Text = value;
                break;
            case Text text:
                text.Content = value;
                break;
            case Button button:
                button.Text = value;
                break;
            default:
                throw new MisuseException($"Element {id} has no value.");
        }
    }

    // --- Arguments ---

    // A statement's handler: the numbers it takes first, then the rest of the line as text.
    private delegate void Handler(int[] numbers, string text, IStatementContext context);

    // Adds a statement that takes <paramref name="numbers"/> numbers and then text to the
    // end of the line (decoded, so a quoted label keeps its spaces). A misuse ends the script
    // with the statement's name and usage.
    private static void Add(YesNtInterpreter interpreter, string name, string usage, int numbers, Handler handler)
    {
        interpreter.AddStatement(
            new StatementInformation(name, SearchMode.StartOfLine, SpaceAround.End),
            (args, context) =>
            {
                try
                {
                    int[] values = ParseArguments(args, numbers, out string text);
                    handler(values, text, context);
                }
                catch (MisuseException ex)
                {
                    context.Exit($"[{name}] {ex.Message} Usage: {usage}", true);
                }
            });
    }

    // A handler for a statement that takes no text after its numbers.
    private static Handler NoText(Action<int[], IStatementContext> handler)
    {
        return (numbers, text, context) =>
        {
            if (text.Length > 0)
            {
                throw new MisuseException($"Unexpected {text}.");
            }

            handler(numbers, context);
        };
    }

    private static int[] ParseArguments(string args, int count, out string text)
    {
        int[] values = new int[count];
        int at = 0;
        for (int i = 0; i < count; i++)
        {
            while (at < args.Length && args[at] == ' ')
            {
                at++;
            }

            int end = args.IndexOf(' ', at);
            if (end < 0)
            {
                end = args.Length;
            }

            if (end == at)
            {
                throw new MisuseException("Missing arguments.");
            }

            values[i] = Number(args[at..end].FromSafeString(), "argument");
            at = end;
        }

        text = args[at..].Trim().FromSafeString();
        return values;
    }

    // Any number, rounded: calc gives fractions (a percentage, half a width), and pixels and
    // a slider's steps are whole.
    private static int Number(string text, string what)
    {
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && Math.Abs(value) < int.MaxValue
            ? (int)Math.Round(value)
            : throw new MisuseException($"The {what} {text} is not a number.");
    }

    private static int Positive(int value, string what)
    {
        return value > 0 ? value : throw new MisuseException($"The {what} {value} is not above 0.");
    }

    private static int Channel(int value)
    {
        return value is >= 0 and <= 255 ? value : throw new MisuseException($"The colour value {value} is not from 0 to 255.");
    }

    private static bool Bool(string text)
    {
        return bool.TryParse(text, out bool value) ? value : throw new MisuseException($"Expected True or False, not \"{text}\".");
    }

    private Window GetWindow(int id)
    {
        return windows.TryGetValue(id, out Window? window) ? window : throw new MisuseException($"No window {id}.");
    }

    private UIElement GetElement(int id)
    {
        return elements.TryGetValue(id, out UIElement? element) ? element : throw new MisuseException($"No element {id}.");
    }

    private int AddElement(UIElement element, IStatementContext context)
    {
        int id = nextId++;
        elements[id] = element;
        context.SetOut(id.ToString(CultureInfo.InvariantCulture));
        return id;
    }

    // A script used a statement wrongly: the script ends with this message.
    private sealed class MisuseException(string message) : Exception(message);
}
