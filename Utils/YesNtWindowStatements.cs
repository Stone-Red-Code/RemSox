using System;
using System.Collections.Generic;
using System.Drawing;
using RemSox.Processing;
using RemSox.UI.GUI.Windows;
using YesNt.Interpreter.Runtime;
using YesNt.Interpreter.Utilities;

namespace RemSox.Utils
{
    /// <summary>
    /// Registers all YesNt statements that expose the WindowManager / UI element API.
    ///
    /// Window management:
    ///   win_create "My Window" 320 240       creates a window, read back id with %win_last_id
    ///   win_close  ${myWinId}                closes/destroys a window by id
    ///   win_flush  ${myWinId}                forces a redraw of the window
    ///   win_title  ${myWinId} "New Title"    changes the window title
    ///   win_autoflush ${myWinId} true        enables/disables auto-flush
    ///   win_invalidate_all                   invalidates and redraws every window
    ///
    /// UI element creation (read back id with %ui_last_id after each call):
    ///   ui_button   ${myWinId} 20 30 100 30 "Click Me"
    ///   ui_checkbox ${myWinId} 20 80 "Check Me" true
    ///   ui_label    ${myWinId} 20 10 "Hello World"
    ///   ui_textbox  ${myWinId} 20 50 160 24 "placeholder"
    ///   ui_line     ${myWinId} 20 130 180 130 255 0 0   (x1 y1 x2 y2 R G B)
    ///   ui_rect     ${myWinId} 20 20 100 60 0 128 255   (x y w h R G B)
    ///
    /// Inline substitutions (use inside any line, like %read_line):
    ///   %win_last_id   expands to the id of the last created window
    ///   %ui_last_id    expands to the id of the last created UI element
    /// </summary>
    public static class YesNtWindowStatements
    {
        // Maps script-visible integer ids to actual Window objects.
        private static readonly Dictionary<int, Window> s_windows = new();
        private static int s_nextWindowId = 1;
        private static int s_lastWindowId = 0;

        private static int s_nextUiId = 1;
        private static int s_lastUiId = 0;

        private static Process s_ownerProcess = null!;

        /// <summary>
        /// Call this from <see cref="YesNtInterpreterProcess.Start"/> to register
        /// every window-related statement with the given interpreter.
        /// </summary>
        /// <param name="interpreter">The live interpreter instance.</param>
        /// <param name="ownerProcess">
        ///   The <see cref="Process"/> that will own created windows
        ///   (usually the <see cref="YesNtInterpreterProcess"/> itself).
        /// </param>
        public static void Register(YesNtInterpreter interpreter, Process ownerProcess)
        {
            s_ownerProcess = ownerProcess;

            RegisterWindowStatements(interpreter);
            RegisterButtonStatement(interpreter);
            RegisterCheckBoxStatement(interpreter);
            RegisterLabelStatement(interpreter);
            RegisterTextBoxStatement(interpreter);
            RegisterLineStatement(interpreter);
            RegisterRectStatement(interpreter);
            RegisterLastIdSubstitutions(interpreter);
        }

        private static void RegisterWindowStatements(YesNtInterpreter interpreter)
        {
            // win_create "Title" width height
            // Sets %win_last_id to the new window's id.
            //
            //   win_create "Control Test" 320 240
            //   global winId = %win_last_id
            interpreter.AddStatement(
                new StatementInformation(
                    "win_create",
                    YesNt.Interpreter.Enums.SearchMode.StartOfLine,
                    YesNt.Interpreter.Enums.SpaceAround.End)
                {
                    Priority = YesNt.Interpreter.Enums.Priority.Normal
                },
                (args, context) =>
                {
                    string trimmed = args.Trim();
                    if (!TryParseWindowArgs(trimmed, out string title, out int w, out int h))
                    {
                        context.Exit($"[win_create] Invalid arguments: {trimmed}", false);
                        return;
                    }

                    Window win = WindowManager.CreateWindow(
                        s_ownerProcess,
                        title,
                        new Size(w, h));

                    int id = s_lastWindowId = s_nextWindowId++;
                    s_windows[id] = win;
                });

            // win_close <id>
            interpreter.AddStatement(
                new StatementInformation(
                    "win_close",
                    YesNt.Interpreter.Enums.SearchMode.StartOfLine,
                    YesNt.Interpreter.Enums.SpaceAround.End)
                {
                    Priority = YesNt.Interpreter.Enums.Priority.Normal
                },
                (args, context) =>
                {
                    string idStr = args.Trim();
                    if (int.TryParse(idStr, out int id) && s_windows.TryGetValue(id, out Window? win))
                    {
                        WindowManager.CloseWindow(win);
                        s_windows.Remove(id);
                    }
                });

            // win_flush <id>
            interpreter.AddStatement(
                new StatementInformation(
                    "win_flush",
                    YesNt.Interpreter.Enums.SearchMode.StartOfLine,
                    YesNt.Interpreter.Enums.SpaceAround.End)
                {
                    Priority = YesNt.Interpreter.Enums.Priority.Normal
                },
                (args, context) =>
                {
                    string idStr = args.Trim();
                    if (int.TryParse(idStr, out int id) && s_windows.TryGetValue(id, out Window? win))
                    {
                        win.Flush();
                    }
                });

            // win_title <id> "New Title"
            interpreter.AddStatement(
                new StatementInformation(
                    "win_title",
                    YesNt.Interpreter.Enums.SearchMode.StartOfLine,
                    YesNt.Interpreter.Enums.SpaceAround.End)
                {
                    Priority = YesNt.Interpreter.Enums.Priority.Normal
                },
                (args, context) =>
                {
                    string rest = args.Trim();
                    int spaceIdx = rest.IndexOf(' ');
                    if (spaceIdx < 0) { return; }

                    string idStr = rest[..spaceIdx].Trim();
                    string newTitle = rest[(spaceIdx + 1)..].Trim().Trim('"');

                    if (int.TryParse(idStr, out int id) && s_windows.TryGetValue(id, out Window? win))
                    {
                        win.Title = newTitle;
                    }
                });

            // win_autoflush <id> true|false
            interpreter.AddStatement(
                new StatementInformation(
                    "win_autoflush",
                    YesNt.Interpreter.Enums.SearchMode.StartOfLine,
                    YesNt.Interpreter.Enums.SpaceAround.End)
                {
                    Priority = YesNt.Interpreter.Enums.Priority.Normal
                },
                (args, context) =>
                {
                    string rest = args.Trim();
                    string[] parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0], out int id)
                        && bool.TryParse(parts[1], out bool enabled)
                        && s_windows.TryGetValue(id, out Window? win))
                    {
                        win.AutoFlush = enabled;
                    }
                });

            // win_invalidate_all
            interpreter.AddStatement(
                new StatementInformation(
                    "win_invalidate_all",
                    YesNt.Interpreter.Enums.SearchMode.Contains,
                    YesNt.Interpreter.Enums.SpaceAround.None)
                {
                    Priority = YesNt.Interpreter.Enums.Priority.Normal
                },
                (args, context) =>
                {
                    WindowManager.InvalidateAll();
                });
        }

        // ui_button <winId> <x> <y> <w> <h> "Label" [R G B]
        //   ui_button ${winId} 20 30 100 30 "Click Me"
        //   ui_button ${winId} 20 30 100 30 "Click Me" 173 216 230
        private static void RegisterButtonStatement(YesNtInterpreter interpreter)
        {
            interpreter.AddStatement(
                new StatementInformation(
                    "ui_button",
                    YesNt.Interpreter.Enums.SearchMode.StartOfLine,
                    YesNt.Interpreter.Enums.SpaceAround.End)
                {
                    Priority = YesNt.Interpreter.Enums.Priority.Normal
                },
                (args, context) =>
                {
                    string rest = args.Trim();
                    if (!TryParseUiArgs(rest, 5, out int winId, out int[] nums, out string label, out Color color))
                    {
                        context.Exit($"[ui_button] Invalid arguments: {rest}", false);
                        return;
                    }

                    if (!s_windows.TryGetValue(winId, out Window? win))
                    {
                        context.Exit($"[ui_button] No window with id {winId}", false);
                        return;
                    }

                    int uiId = s_lastUiId = s_nextUiId++;
                    _ = win.CreateUIElement<UI.GUI.UIEelements.Controls.Button>(b =>
                    {
                        b.Position = new Point(nums[0], nums[1]);
                        b.Size = new Size(nums[2], nums[3]);
                        b.Text = label;
                        if (color != Color.Empty)
                            b.BackgroundColor = color;
                    });
                });
        }

        // ui_checkbox <winId> <x> <y> "Label" true|false
        //   ui_checkbox ${winId} 20 80 "Enable feature" false
        private static void RegisterCheckBoxStatement(YesNtInterpreter interpreter)
        {
            interpreter.AddStatement(
                new StatementInformation(
                    "ui_checkbox",
                    YesNt.Interpreter.Enums.SearchMode.StartOfLine,
                    YesNt.Interpreter.Enums.SpaceAround.End)
                {
                    Priority = YesNt.Interpreter.Enums.Priority.Normal
                },
                (args, context) =>
                {
                    string rest = args.Trim();
                    if (!TryParseCheckboxArgs(rest, out int winId, out int x, out int y, out string label, out bool isChecked))
                    {
                        context.Exit($"[ui_checkbox] Invalid arguments: {rest}", false);
                        return;
                    }

                    if (!s_windows.TryGetValue(winId, out Window? win))
                    {
                        context.Exit($"[ui_checkbox] No window with id {winId}", false);
                        return;
                    }

                    int uiId = s_lastUiId = s_nextUiId++;
                    _ = win.CreateUIElement<UI.GUI.UIEelements.Controls.CheckBox>(c =>
                    {
                        c.Position = new Point(x, y);
                        c.Text = label;
                        c.IsChecked = isChecked;
                    });
                });
        }

        // ui_label <winId> <x> <y> "Text"
        //   ui_label ${winId} 10 10 "Hello, World!"
        private static void RegisterLabelStatement(YesNtInterpreter interpreter)
        {
            interpreter.AddStatement(
                new StatementInformation(
                    "ui_label",
                    YesNt.Interpreter.Enums.SearchMode.StartOfLine,
                    YesNt.Interpreter.Enums.SpaceAround.End)
                {
                    Priority = YesNt.Interpreter.Enums.Priority.Normal
                },
                (args, context) =>
                {
                    string rest = args.Trim();
                    if (!TryParseUiArgs(rest, 2, out int winId, out int[] nums, out string text, out _))
                    {
                        context.Exit($"[ui_label] Invalid arguments: {rest}", false);
                        return;
                    }

                    if (!s_windows.TryGetValue(winId, out Window? win))
                    {
                        context.Exit($"[ui_label] No window with id {winId}", false);
                        return;
                    }

                    //int uiId = s_lastUiId = s_nextUiId++;
                    //_ = win.CreateUIElement<UI.GUI.UIEelements.Controls.Label>(l =>
                    //{
                    //    l.Position = new Point(nums[0], nums[1]);
                    //    l.Text = text;
                    //});
                });
        }

        // ui_textbox <winId> <x> <y> <w> <h> "Placeholder"
        //   ui_textbox ${winId} 20 50 160 24 "Enter name..."
        private static void RegisterTextBoxStatement(YesNtInterpreter interpreter)
        {
            interpreter.AddStatement(
                new StatementInformation(
                    "ui_textbox",
                    YesNt.Interpreter.Enums.SearchMode.StartOfLine,
                    YesNt.Interpreter.Enums.SpaceAround.End)
                {
                    Priority = YesNt.Interpreter.Enums.Priority.Normal
                },
                (args, context) =>
                {
                    string rest = args.Trim();
                    if (!TryParseUiArgs(rest, 4, out int winId, out int[] nums, out string placeholder, out _))
                    {
                        context.Exit($"[ui_textbox] Invalid arguments: {rest}", false);
                        return;
                    }

                    if (!s_windows.TryGetValue(winId, out Window? win))
                    {
                        context.Exit($"[ui_textbox] No window with id {winId}", false);
                        return;
                    }

                    //int uiId = s_lastUiId = s_nextUiId++;
                    //_ = win.CreateUIElement<UI.GUI.UIEelements.Controls.TextBox>(t =>
                    //{
                    //    t.Position = new Point(nums[0], nums[1]);
                    //    t.Size = new Size(nums[2], nums[3]);
                    //    t.PlaceholderText = placeholder;
                    //});
                });
        }

        // ui_line <winId> <x1> <y1> <x2> <y2> <R> <G> <B>
        //   ui_line ${winId} 20 130 180 130 255 0 0
        private static void RegisterLineStatement(YesNtInterpreter interpreter)
        {
            interpreter.AddStatement(
                new StatementInformation(
                    "ui_line",
                    YesNt.Interpreter.Enums.SearchMode.StartOfLine,
                    YesNt.Interpreter.Enums.SpaceAround.End)
                {
                    Priority = YesNt.Interpreter.Enums.Priority.Normal
                },
                (args, context) =>
                {
                    string rest = args.Trim();
                    string[] parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 8
                        || !int.TryParse(parts[0], out int winId)
                        || !int.TryParse(parts[1], out int x1)
                        || !int.TryParse(parts[2], out int y1)
                        || !int.TryParse(parts[3], out int x2)
                        || !int.TryParse(parts[4], out int y2)
                        || !int.TryParse(parts[5], out int r)
                        || !int.TryParse(parts[6], out int g)
                        || !int.TryParse(parts[7], out int b))
                    {
                        context.Exit($"[ui_line] Invalid arguments: {rest}", false);
                        return;
                    }

                    if (!s_windows.TryGetValue(winId, out Window? win))
                    {
                        context.Exit($"[ui_line] No window with id {winId}", false);
                        return;
                    }

                    int uiId = s_lastUiId = s_nextUiId++;
                    _ = win.CreateUIElement<UI.GUI.UIEelements.Shapes.Line>(l =>
                    {
                        l.Position = new Point(x1, y1);
                        l.EndPosition = new Point(x2, y2);
                        l.Color = Color.FromArgb(r, g, b);
                    });
                });
        }

        // ui_rect <winId> <x> <y> <w> <h> <R> <G> <B>
        //   ui_rect ${winId} 20 20 100 60 0 128 255
        private static void RegisterRectStatement(YesNtInterpreter interpreter)
        {
            interpreter.AddStatement(
                new StatementInformation(
                    "ui_rect",
                    YesNt.Interpreter.Enums.SearchMode.StartOfLine,
                    YesNt.Interpreter.Enums.SpaceAround.End)
                {
                    Priority = YesNt.Interpreter.Enums.Priority.Normal
                },
                (args, context) =>
                {
                    string rest = args.Trim();
                    string[] parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 8
                        || !int.TryParse(parts[0], out int winId)
                        || !int.TryParse(parts[1], out int x)
                        || !int.TryParse(parts[2], out int y)
                        || !int.TryParse(parts[3], out int w)
                        || !int.TryParse(parts[4], out int h)
                        || !int.TryParse(parts[5], out int r)
                        || !int.TryParse(parts[6], out int g)
                        || !int.TryParse(parts[7], out int b))
                    {
                        context.Exit($"[ui_rect] Invalid arguments: {rest}", false);
                        return;
                    }

                    if (!s_windows.TryGetValue(winId, out Window? win))
                    {
                        context.Exit($"[ui_rect] No window with id {winId}", false);
                        return;
                    }

                    int uiId = s_lastUiId = s_nextUiId++;
                    _ = win.CreateUIElement<UI.GUI.UIEelements.Shapes.Rectangle>(rect =>
                    {
                        rect.Position = new Point(x, y);
                        rect.Size = new Size(w, h);
                        rect.Color = Color.FromArgb(r, g, b);
                    });
                });
        }

        private static void RegisterLastIdSubstitutions(YesNtInterpreter interpreter)
        {
            // %win_last_id → replaced with the id of the last created window
            interpreter.AddStatement(
                new StatementInformation(
                    "%win_last_id",
                    YesNt.Interpreter.Enums.SearchMode.Contains,
                    YesNt.Interpreter.Enums.SpaceAround.None)
                {
                    KeepStatementInArgs = true,
                    Priority = YesNt.Interpreter.Enums.Priority.PreProcessing
                },
                (args, context) =>
                {
                    context.CurrentLine = TemplateProcessor.ProcessSimplePlaceholders(
                        args, "%win_last_id", s_lastWindowId.ToString());
                });

            // %ui_last_id → replaced with the id of the last created UI element
            interpreter.AddStatement(
                new StatementInformation(
                    "%ui_last_id",
                    YesNt.Interpreter.Enums.SearchMode.Contains,
                    YesNt.Interpreter.Enums.SpaceAround.None)
                {
                    KeepStatementInArgs = true,
                    Priority = YesNt.Interpreter.Enums.Priority.PreProcessing
                },
                (args, context) =>
                {
                    context.CurrentLine = TemplateProcessor.ProcessSimplePlaceholders(
                        args, "%ui_last_id", s_lastUiId.ToString());
                });
        }

        /// <summary>
        /// Parses: "Title" width height
        /// The title may be quoted or unquoted.
        /// </summary>
        private static bool TryParseWindowArgs(string input, out string title, out int w, out int h)
        {
            title = string.Empty; w = 0; h = 0;

            input = input.Trim();
            string remaining;

            if (input.StartsWith('"'))
            {
                int end = input.IndexOf('"', 1);
                if (end < 0) return false;
                title = input[1..end];
                remaining = input[(end + 1)..].Trim();
            }
            else
            {
                int space = input.IndexOf(' ');
                if (space < 0) return false;
                title = input[..space];
                remaining = input[(space + 1)..].Trim();
            }

            string[] nums = remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return nums.Length >= 2
                && int.TryParse(nums[0], out w)
                && int.TryParse(nums[1], out h);
        }

        /// <summary>
        /// Generic UI arg parser.
        /// Input format: winId n0 n1 ... n{numCount-1} "Label" [R G B]
        /// </summary>
        private static bool TryParseUiArgs(
            string input,
            int numCount,
            out int winId,
            out int[] nums,
            out string label,
            out Color color)
        {
            winId = 0; nums = Array.Empty<int>(); label = string.Empty; color = Color.Empty;

            string[] parts = input.Split(' ', numCount + 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < numCount + 1) return false;

            if (!int.TryParse(parts[0], out winId)) return false;

            nums = new int[numCount];
            for (int i = 0; i < numCount; i++)
            {
                if (!int.TryParse(parts[i + 1], out nums[i])) return false;
            }

            string rest = string.Join(" ", parts[(numCount + 1)..]).Trim();

            if (rest.StartsWith('"'))
            {
                int end = rest.IndexOf('"', 1);
                if (end < 0) return false;
                label = rest[1..end];
                rest = rest[(end + 1)..].Trim();
            }
            else
            {
                int sp = rest.IndexOf(' ');
                label = sp < 0 ? rest : rest[..sp];
                rest = sp < 0 ? string.Empty : rest[(sp + 1)..].Trim();
            }

            if (!string.IsNullOrWhiteSpace(rest))
            {
                string[] rgb = rest.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (rgb.Length >= 3
                    && int.TryParse(rgb[0], out int r)
                    && int.TryParse(rgb[1], out int g)
                    && int.TryParse(rgb[2], out int b))
                {
                    color = Color.FromArgb(r, g, b);
                }
            }

            return true;
        }

        /// <summary>Parses: winId x y "Label" true|false</summary>
        private static bool TryParseCheckboxArgs(
            string input,
            out int winId,
            out int x, out int y,
            out string label,
            out bool isChecked)
        {
            winId = 0; x = 0; y = 0; label = string.Empty; isChecked = false;

            string[] parts = input.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return false;

            if (!int.TryParse(parts[0], out winId)
                || !int.TryParse(parts[1], out x)
                || !int.TryParse(parts[2], out y)) return false;

            string rest = parts[3].Trim();
            if (rest.StartsWith('"'))
            {
                int end = rest.IndexOf('"', 1);
                if (end < 0) return false;
                label = rest[1..end];
                rest = rest[(end + 1)..].Trim();
            }
            else
            {
                int sp = rest.IndexOf(' ');
                if (sp < 0) { label = rest; rest = "false"; }
                else { label = rest[..sp]; rest = rest[(sp + 1)..].Trim(); }
            }

            bool.TryParse(rest, out isChecked);
            return true;
        }
    }
}