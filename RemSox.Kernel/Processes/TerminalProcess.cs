using Cosmos.Kernel.System.Input;

using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;
using RemSox.Kernel.UI.GUI.UIEelements.Controls;
using RemSox.Kernel.UI.GUI.UIEelements.Shapes;

using Shapes = RemSox.Kernel.UI.GUI.UIEelements.Shapes;
using RemSox.Kernel.UI.GUI.Windows;

using System.Drawing;
using RemSox.Kernel.Utils;
using RemSox.Shared.UI;

namespace RemSox.Kernel.Processes;

/// <summary>
/// A shell in a desktop window. Each Terminal window is a shell of its own, with its own working
/// directory and history. Full-screen programs such as <c>edit</c> get the window as a text screen.
/// </summary>
public sealed class TerminalProcess() : Process("Terminal"), IShellTerminal
{
    private const int LineHeight = 30;
    private static readonly Color OutputColor = Color.LightGreen;
    private static readonly Color ErrorColor = Color.FromArgb(255, 110, 100);

    private static int terminalCount;

    private Window window = null!;
    private ShellFrontEnd frontEnd = null!;
    private readonly List<(string Text, Color Color)> outputLines = [];
    private readonly List<Text> textLines = [];
    private Size lastSize = new(-1, -1);
    private bool displayDirty = true;

    // The line being typed, its prompt included; null while a command runs.
    private string? inputLine;

    // Scrollback: how many rows the view is scrolled up from the bottom, and the shell's row
    // count and width at the last redraw (a page, and where new output wraps).
    private const int WheelRows = 3;
    private int scrollRows;
    private int shellRowCount = 2;
    private int shellColumns = 1;

    // How many rows each output line wraps to at shellColumns, and their sum, so the scroll bar
    // knows the length of the whole output without wrapping every line on each redraw.
    private readonly List<int> lineRowCounts = [];
    private int outputRowCount;

    // The scroll bar along the right edge, with a gap that keeps it clear of the window's resize border.
    private const int ScrollBarWidth = 10;
    private const int ScrollBarGap = 6;
    private ScrollBar scrollBar = null!;

    // Text screen mode: a grid of cells a full-screen program draws into, in rows smaller than
    // the shell's so the default window holds a usable editor.
    private const int ScreenLineHeight = 20;
    private const int ScreenTop = 22;
    private static readonly Color InverseBackground = Color.FromArgb(200, 200, 200);
    private static readonly Color CursorColor = Color.White;
    private bool screenMode;
    private string[] screenRows = [];
    private bool[] screenInverse = [];
    private int cursorColumn, cursorRow;
    private readonly List<(Shapes.Rectangle Background, Text Text, bool Inverse)> screenElements = [];
    private Shapes.Rectangle? cursorBar;

    internal override void Start(string[] args)
    {
        frontEnd = new ShellFrontEnd(new ShellSession("terminal" + ++terminalCount, canExit: true, WorkingDirectory), this);
        frontEnd.CommandFinished += _ =>
        {
            if (frontEnd.Session.ExitRequested)
            {
                RequestStop();
            }
        };

        window = CreateWindow("Terminal", new Size(400, 300));

        WriteLine("RemSox GUI Terminal v1.0");
        WriteLine("Type 'help' for commands.");

        scrollBar = window.CreateUIElement<ScrollBar>(b => b.BackgroundColor = Color.FromArgb(48, 48, 48));
        scrollBar.Scrolled += value =>
        {
            // The bar counts from the top, scrollRows from the bottom.
            scrollRows = scrollBar.Maximum - scrollBar.ViewportSize - value;
            displayDirty = true;
        };

        window.Flush();
        window.OnKeyEvent += HandleKey;
        window.OnMouseEvent += HandleMouse;

        // Started with a command line (terminal ls /, or the start menu opening a script or a
        // file in edit): run it as if typed, then close like cmd /c, unless it failed, so the
        // error stays readable with a prompt to carry on from.
        if (args.Length > 0)
        {
            _ = RunAndCloseAsync(string.Join(' ', args.Select(CommandLine.Quote)));
        }
        else
        {
            frontEnd.Start();
        }
    }

    internal override void Tick()
    {
        // A size counts once the user lets go of the frame, so a full-screen program redraws
        // once per resize rather than for every step of the drag.
        if (window.Size != lastSize && !window.IsResizing)
        {
            lastSize = window.Size;
            displayDirty = true;
            Resized?.Invoke();
        }

        // Redraw at most once per tick: a command printing many lines would otherwise redraw
        // (and send to remote viewers) the whole terminal once per line.
        if (displayDirty)
        {
            displayDirty = false;
            UpdateDisplay();
        }
    }

    internal override void Stop()
    {
        window.OnKeyEvent -= HandleKey;
        window.OnMouseEvent -= HandleMouse;
        frontEnd.Close();
    }

    private void HandleKey(KeyEvent keyEvent)
    {
        // Page Up and Page Down scroll the shell's output; a full-screen program gets them.
        if (!screenMode && keyEvent.Key is Key.PageUp or Key.PageDown)
        {
            int page = Math.Max(1, shellRowCount - 1);
            ScrollBy(keyEvent.Key == Key.PageUp ? page : -page);
            return;
        }

        // Typing brings the prompt back into view.
        if (scrollRows > 0)
        {
            scrollRows = 0;
            displayDirty = true;
        }

        frontEnd.HandleKey(ToConsoleKey(keyEvent));
    }

    private void HandleMouse(MouseEvent mouseEvent)
    {
        // The wheel's delta is in notches, positive downwards.
        if (!screenMode && mouseEvent.Type == MouseEventType.Wheel && mouseEvent.Delta != 0)
        {
            ScrollBy(-mouseEvent.Delta * WheelRows);
        }
    }

    /// <summary>Scrolls the output up (positive) or down; the redraw stops it at the oldest row.</summary>
    private void ScrollBy(int rows)
    {
        int scrolled = Math.Max(0, scrollRows + rows);
        if (scrolled != scrollRows)
        {
            scrollRows = scrolled;
            displayDirty = true;
        }
    }

    private async Task RunAndCloseAsync(string line)
    {
        int exitCode = await frontEnd.RunAsync(line);

        // Ctrl+C is the user ending it, not a failure to read about.
        if (exitCode is CommandResult.Success or CommandResult.Cancelled)
        {
            RequestStop();
        }
    }

    private void AddLine(string text, Color color)
    {
        int rows = TextWrap.Wrap(text, shellColumns).Count;
        outputLines.Add((text, color));
        lineRowCounts.Add(rows);
        outputRowCount += rows;

        // While scrolled back, keep the same rows in view as output arrives below them.
        if (scrollRows > 0)
        {
            scrollRows += rows;
        }

        if (outputLines.Count > 1000)
        {
            outputLines.RemoveAt(0);
            outputRowCount -= lineRowCounts[0];
            lineRowCounts.RemoveAt(0);
        }

        displayDirty = true;
    }

    // --- IShellTerminal ---

    public void WriteLine(string text)
    {
        AddLine(text, OutputColor);
    }

    public void WriteError(string text)
    {
        AddLine(text, ErrorColor);
    }

    public void Clear()
    {
        outputLines.Clear();
        lineRowCounts.Clear();
        outputRowCount = 0;
        scrollRows = 0;

        displayDirty = true;
    }

    public void ShowInput(string? line)
    {
        inputLine = line;
        displayDirty = true;
    }

    public void CommitInput()
    {
        AddLine(inputLine ?? string.Empty, OutputColor);
        inputLine = string.Empty;
    }

    /// <summary>Raised on the main loop once the window has been resized.</summary>
    public event Action? Resized;

    private static int ScreenCellWidth => Utils.CanvasExtensions.GetCellWidth(ScreenLineHeight);

    public int Width => Math.Max(1, (window.Size.Width - 10) / ScreenCellWidth);

    public int Height => Math.Max(1, (window.Size.Height - ScreenTop - 4) / ScreenLineHeight);

    public void BeginScreen()
    {
        screenMode = true;
        screenRows = [];
        cursorColumn = cursorRow = 0;

        displayDirty = true;
    }

    public void EndScreen()
    {
        screenMode = false;

        displayDirty = true;
    }

    public void WriteAt(int column, int row, string text, bool inverse)
    {
        FitScreenBuffer();

        if (row < 0 || row >= screenRows.Length || column < 0 || column >= screenRows[row].Length)
        {
            return;
        }

        string line = screenRows[row];
        int length = Math.Min(text.Length, line.Length - column);
        screenRows[row] = string.Concat(line.AsSpan(0, column), text.AsSpan(0, length), line.AsSpan(column + length));

        if (column == 0)
        {
            screenInverse[row] = inverse;
        }

        displayDirty = true;
    }

    public void SetCursor(int column, int row)
    {
        cursorColumn = column;
        cursorRow = row;
        displayDirty = true;
    }

    /// <summary>Resizes the cell buffer to the window, blanking it when the size changed.</summary>
    private void FitScreenBuffer()
    {
        int width = Width;
        int height = Height;

        if (screenRows.Length != height || (height > 0 && screenRows[0].Length != width))
        {
            screenRows = [.. Enumerable.Repeat(new string(' ', width), height)];
            screenInverse = new bool[height];
        }
    }

    /// <summary>A window key event as the console key a full-screen program reads.</summary>
    private static ConsoleKeyInfo ToConsoleKey(KeyEvent keyEvent)
    {
        ConsoleKey key = keyEvent.Key switch
        {
            Key.UpArrow => ConsoleKey.UpArrow,
            Key.DownArrow => ConsoleKey.DownArrow,
            Key.LeftArrow => ConsoleKey.LeftArrow,
            Key.RightArrow => ConsoleKey.RightArrow,
            Key.Home => ConsoleKey.Home,
            Key.End => ConsoleKey.End,
            Key.PageUp => ConsoleKey.PageUp,
            Key.PageDown => ConsoleKey.PageDown,
            Key.Insert => ConsoleKey.Insert,
            Key.Delete => ConsoleKey.Delete,
            Key.Backspace => ConsoleKey.Backspace,
            Key.Enter or Key.NumEnter => ConsoleKey.Enter,
            Key.Tab => ConsoleKey.Tab,
            Key.Escape => ConsoleKey.Escape,
            Key.Spacebar => ConsoleKey.Spacebar,
            _ => LetterKeys.TryGetValue(keyEvent.Key, out ConsoleKey letter) ? letter : LetterKey(keyEvent.KeyChar),
        };

        ConsoleModifiers modifiers = keyEvent.Modifiers;
        return new ConsoleKeyInfo(keyEvent.KeyChar, key,
            modifiers.HasFlag(ConsoleModifiers.Shift), modifiers.HasFlag(ConsoleModifiers.Alt), modifiers.HasFlag(ConsoleModifiers.Control));
    }

    // The letter keys by key rather than by character: with Ctrl held a window key event carries no
    // usable character, and Ctrl+S must still read as S with Control.
    private static readonly Dictionary<Key, ConsoleKey> LetterKeys = new()
    {
        [Key.A] = ConsoleKey.A, [Key.B] = ConsoleKey.B, [Key.C] = ConsoleKey.C, [Key.D] = ConsoleKey.D,
        [Key.E] = ConsoleKey.E, [Key.F] = ConsoleKey.F, [Key.G] = ConsoleKey.G, [Key.H] = ConsoleKey.H,
        [Key.I] = ConsoleKey.I, [Key.J] = ConsoleKey.J, [Key.K] = ConsoleKey.K, [Key.L] = ConsoleKey.L,
        [Key.M] = ConsoleKey.M, [Key.N] = ConsoleKey.N, [Key.O] = ConsoleKey.O, [Key.P] = ConsoleKey.P,
        [Key.Q] = ConsoleKey.Q, [Key.R] = ConsoleKey.R, [Key.S] = ConsoleKey.S, [Key.T] = ConsoleKey.T,
        [Key.U] = ConsoleKey.U, [Key.V] = ConsoleKey.V, [Key.W] = ConsoleKey.W, [Key.X] = ConsoleKey.X,
        [Key.Y] = ConsoleKey.Y, [Key.Z] = ConsoleKey.Z,
    };

    /// <summary>The letter key for a typed letter or its Ctrl control character, for keys the table does not cover.</summary>
    private static ConsoleKey LetterKey(char keyChar)
    {
        char letter = keyChar is >= '\x01' and <= '\x1a' ? (char)('A' + keyChar - 1) : char.ToUpperInvariant(keyChar);
        return letter is >= 'A' and <= 'Z' ? (ConsoleKey)letter : 0;
    }

    // --- Display ---

    /// <summary>The bottom line: the line being typed with its cursor, or nothing while a command runs.</summary>
    private string GetInputLine()
    {
        return inputLine is null ? string.Empty : inputLine + "_";
    }

    private void UpdateDisplay()
    {
        if (window is null)
        {
            return;
        }

        if (screenMode)
        {
            UpdateScreenDisplay();
            return;
        }

        HideScreenElements();

        int availableHeight = window.Size.Height - 24; // 18 for title + 6 margin
        int rowCount = Math.Max(2, availableHeight / LineHeight); // output rows + the input line
        int textWidth = window.Size.Width - 10 - ScrollBarWidth - ScrollBarGap;
        int columns = Math.Max(1, textWidth / Utils.CanvasExtensions.GetCellWidth(LineHeight));

        shellRowCount = rowCount;
        if (columns != shellColumns)
        {
            shellColumns = columns;
            CountOutputRows();
        }

        List<(string Text, Color Color)> rows = GetVisibleRows(rowCount, columns);
        UpdateScrollBar(rowCount, columns);

        // One Text element per row on screen
        while (textLines.Count < rowCount)
        {
            Text textElement = window.CreateUIElement<Text>(t =>
            {
                t.Color = OutputColor;
                t.Content = "";
                t.FontSize = LineHeight;
            });
            textLines.Add(textElement);
        }

        // Align everything flush to the bottom margin
        int startY = Math.Max(20, window.Size.Height - (rowCount * LineHeight) - 5);

        for (int i = 0; i < rowCount; i++)
        {
            textLines[i].Content = rows[i].Text;
            textLines[i].Color = rows[i].Color;
            textLines[i].Position = new Point(5, startY + (i * LineHeight));
        }

        // Hide the Text elements a smaller window no longer needs
        for (int i = rowCount; i < textLines.Count; i++)
        {
            textLines[i].Content = "";
        }

        window.Flush();
    }

    /// <summary>Counts again how many rows each output line wraps to, after the width changed.</summary>
    private void CountOutputRows()
    {
        outputRowCount = 0;
        for (int i = 0; i < outputLines.Count; i++)
        {
            lineRowCounts[i] = TextWrap.Wrap(outputLines[i].Text, shellColumns).Count;
            outputRowCount += lineRowCounts[i];
        }
    }

    /// <summary>Sets the scroll bar to the whole output (and the input line) and the part in view.</summary>
    private void UpdateScrollBar(int rowCount, int columns)
    {
        int totalRows = outputRowCount + TextWrap.Wrap(GetInputLine(), columns, keepWords: false).Count;

        scrollBar.Position = new Point(window.Size.Width - ScrollBarWidth - ScrollBarGap, Window.TitleBarHeight + 2);
        scrollBar.Size = new Size(ScrollBarWidth, Math.Max(0, window.Size.Height - Window.TitleBarHeight - 2 - ScrollBarGap));
        scrollBar.Maximum = totalRows;
        scrollBar.ViewportSize = rowCount;
        scrollBar.Value = totalRows - rowCount - scrollRows;
    }

    /// <summary>
    /// The <paramref name="rowCount"/> screen rows in view, oldest first: the output lines and
    /// then the input line, each wrapped at <paramref name="columns"/> characters, ending
    /// <see cref="scrollRows"/> rows above the bottom. Wrapping happens here rather than when a
    /// line is added, so a resized window re-wraps everything. A long input line keeps its end
    /// (where the cursor is) on screen.
    /// </summary>
    private List<(string Text, Color Color)> GetVisibleRows(int rowCount, int columns)
    {
        // Rows newest first, only as many as the view needs. The line being typed wraps at the
        // column, like a terminal's, so it does not jump around as words grow; output wraps at
        // word boundaries.
        int needed = scrollRows + rowCount;
        List<(string Text, Color Color)> newestFirst = [];

        List<string> inputRows = TextWrap.Wrap(GetInputLine(), columns, keepWords: false);
        for (int i = inputRows.Count - 1; i >= 0 && newestFirst.Count < needed; i--)
        {
            newestFirst.Add((inputRows[i], OutputColor));
        }

        for (int i = outputLines.Count - 1; i >= 0 && newestFirst.Count < needed; i--)
        {
            (string text, Color color) = outputLines[i];
            List<string> wrapped = TextWrap.Wrap(text, columns);
            for (int j = wrapped.Count - 1; j >= 0 && newestFirst.Count < needed; j--)
            {
                newestFirst.Add((wrapped[j], color));
            }
        }

        // Scrolling stops once the oldest row is at the top.
        scrollRows = Math.Min(scrollRows, Math.Max(0, newestFirst.Count - rowCount));

        int end = Math.Min(newestFirst.Count, scrollRows + rowCount);
        List<(string Text, Color Color)> rows = new(rowCount);
        for (int i = end - scrollRows; i < rowCount; i++)
        {
            rows.Add((string.Empty, OutputColor));
        }

        for (int i = end - 1; i >= scrollRows; i--)
        {
            rows.Add(newestFirst[i]);
        }

        return rows;
    }

    /// <summary>Shows the text screen: each row a Text over a background bar for inverse rows, and the cursor bar.</summary>
    private void UpdateScreenDisplay()
    {
        FitScreenBuffer();

        foreach (Text line in textLines)
        {
            line.Content = "";
        }

        scrollBar.Size = Size.Empty;

        int cellWidth = ScreenCellWidth;
        int rowWidth = Width * cellWidth;

        while (screenElements.Count < screenRows.Length)
        {
            // Background first, then the text, so the text draws over it.
            Shapes.Rectangle background = window.CreateUIElement<Shapes.Rectangle>(r =>
            {
                r.Color = InverseBackground;
                r.Size = Size.Empty;
            });
            Text text = window.CreateUIElement<Text>(t =>
            {
                t.Color = OutputColor;
                t.Content = "";
                t.FontSize = ScreenLineHeight;
            });
            screenElements.Add((background, text, false));
        }

        for (int i = 0; i < screenElements.Count; i++)
        {
            (Shapes.Rectangle background, Text text, bool wasInverse) = screenElements[i];
            bool inverse = i < screenRows.Length && screenInverse[i];
            int y = ScreenTop + (i * ScreenLineHeight);
            Size backgroundSize = inverse ? new Size(rowWidth, ScreenLineHeight) : Size.Empty;

            if (background.Size != backgroundSize || background.Position.Y != y || inverse != wasInverse)
            {
                background.Position = new Point(5, y);
                background.Size = backgroundSize;

                // A changed element is drawn last; send the text again so it stays over its bar.
                text.Content = "\u0001";
                screenElements[i] = (background, text, inverse);
            }

            text.Content = i < screenRows.Length ? screenRows[i] : "";
            text.Color = inverse ? Color.Black : OutputColor;
            text.Position = new Point(5, y);
        }

        cursorBar ??= window.CreateUIElement<Shapes.Rectangle>(r => r.Color = CursorColor);
        cursorBar.Position = new Point(5 + (cursorColumn * cellWidth), ScreenTop + ((cursorRow + 1) * ScreenLineHeight) - 3);
        cursorBar.Size = new Size(cellWidth, 2);

        window.Flush();
    }

    /// <summary>Hides what text screen mode showed, once a program has given the window back.</summary>
    private void HideScreenElements()
    {
        for (int i = 0; i < screenElements.Count; i++)
        {
            (Shapes.Rectangle background, Text text, _) = screenElements[i];
            background.Size = Size.Empty;
            text.Content = "";
            screenElements[i] = (background, text, false);
        }

        if (cursorBar is not null)
        {
            cursorBar.Size = Size.Empty;
        }
    }
}
