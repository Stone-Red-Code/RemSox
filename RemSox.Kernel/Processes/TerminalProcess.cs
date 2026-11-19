using Cosmos.Kernel.System.Input;

using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;
using RemSox.Kernel.UI.GUI.UIEelements.Shapes;

using Shapes = RemSox.Kernel.UI.GUI.UIEelements.Shapes;
using RemSox.Kernel.UI.GUI.Windows;

using System.Drawing;

namespace RemSox.Kernel.Processes;

/// <summary>
/// A shell in a desktop window. Each Terminal window is a shell of its own, with its own working
/// directory and history. Full-screen programs such as <c>edit</c> get the window as a text screen.
/// </summary>
public sealed class TerminalProcess() : Process("Terminal"), ICommandConsole, ITextScreen
{
    private const string Prompt = "> ";
    private const int LineHeight = 30;
    private static readonly Color OutputColor = Color.LightGreen;
    private static readonly Color ErrorColor = Color.FromArgb(255, 110, 100);

    private static int terminalCount;

    private Window window = null!;
    private ShellSession session = null!;
    private LineEditor editor = null!;
    private LineEditor answerEditor = null!;
    private CommandRunner runner = null!;
    private readonly List<(string Text, Color Color)> outputLines = [];
    private readonly List<Text> textLines = [];
    private readonly Lock textLinesLock = new();
    private Size lastSize = new(-1, -1);
    private volatile bool displayDirty = true;

    // A command's pending question (ReadLineAsync) and its prompt; the next line typed answers it.
    private volatile TaskCompletionSource<string?>? pendingAnswer;
    private string answerPrompt = string.Empty;

    // Text screen mode (ITextScreen): a grid of cells a full-screen program draws into, in rows
    // smaller than the shell's so the default window holds a usable editor.
    private const int ScreenLineHeight = 20;
    private const int ScreenTop = 22;
    private static readonly Color InverseBackground = Color.FromArgb(200, 200, 200);
    private static readonly Color CursorColor = Color.White;
    private readonly KeyQueue screenKeys = new();
    private volatile bool screenMode;
    private string[] screenRows = [];
    private bool[] screenInverse = [];
    private int cursorColumn, cursorRow;
    private readonly List<(Shapes.Rectangle Background, Text Text, bool Inverse)> screenElements = [];
    private Shapes.Rectangle? cursorBar;

    internal override void Start(string[] args)
    {
        session = new ShellSession("terminal" + Interlocked.Increment(ref terminalCount), canExit: true, WorkingDirectory);
        editor = new LineEditor(session);
        answerEditor = new LineEditor(session);
        runner = new CommandRunner(session, this);

        window = CreateWindow("Terminal", new Size(400, 300));

        WriteLine("RemSox GUI Terminal v1.0");
        WriteLine("Type 'help' for commands.");

        window.Flush();
        window.OnKeyEvent += HandleKey;

        // Started with a command line (terminal ls /, or a shortcut to a .rsh script): run it as if typed.
        if (args.Length > 0)
        {
            string line = string.Join(' ', args.Select(CommandLine.Quote));
            AddLine(Prompt + line, OutputColor);
            _ = RunAsync(line);
        }
    }

    internal override void Tick()
    {
        if (window.Size != lastSize && !window.IsResizing)
        {
            lastSize = window.Size;
            displayDirty = true;
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
        runner.Cancel();
    }

    private void HandleKey(KeyEvent keyEvent)
    {
        if (screenMode)
        {
            // A full-screen program gets every key, Ctrl+C included.
            screenKeys.Enqueue(ToConsoleKey(keyEvent));
            return;
        }

        if (keyEvent.Key == Key.C && keyEvent.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            // Cancels the running command, and with it any question it is asking.
            runner.Cancel();
            return;
        }

        if (pendingAnswer is { } answer)
        {
            if (keyEvent.Key == Key.Enter)
            {
                string text = answerEditor.Submit();
                pendingAnswer = null;
                AddLine(answerPrompt + text, OutputColor);
                _ = answer.TrySetResult(text);
            }
            else
            {
                Edit(answerEditor, keyEvent);
            }

            displayDirty = true;
            return;
        }

        if (runner.IsRunning)
        {
            return;
        }

        if (keyEvent.Key == Key.Enter)
        {
            string line = editor.Submit();
            AddLine(Prompt + line, OutputColor);
            _ = RunAsync(line);
        }
        else
        {
            Edit(editor, keyEvent);
        }

        displayDirty = true;
    }

    private async Task RunAsync(string line)
    {
        _ = await runner.RunAsync(line);

        if (session.ExitRequested)
        {
            RequestStop();
        }

        displayDirty = true;
    }

    private static void Edit(LineEditor lineEditor, KeyEvent keyEvent)
    {
        switch (keyEvent.Key)
        {
            case Key.Backspace:
                lineEditor.Backspace();
                break;

            case Key.UpArrow:
                lineEditor.HistoryPrevious();
                break;

            case Key.DownArrow:
                lineEditor.HistoryNext();
                break;

            default:
                lineEditor.Insert(keyEvent.KeyChar);
                break;
        }
    }

    private void AddLine(string text, Color color)
    {
        lock (textLinesLock)
        {
            outputLines.Add((text, color));
            if (outputLines.Count > 1000)
            {
                outputLines.RemoveAt(0);
            }
        }

        displayDirty = true;
    }

    // --- ICommandConsole ---

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
        lock (textLinesLock)
        {
            outputLines.Clear();
        }

        displayDirty = true;
    }

    public async Task<string?> ReadLineAsync(string prompt, CancellationToken cancellation)
    {
        TaskCompletionSource<string?> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        answerPrompt = prompt;
        answerEditor.Reset();
        pendingAnswer = answer;
        displayDirty = true;

        using (cancellation.Register(() =>
        {
            if (answer.TrySetResult(null))
            {
                pendingAnswer = null;
                displayDirty = true;
            }
        }))
        {
            return await answer.Task;
        }
    }

    // --- ITextScreen ---

    public ITextScreen? Screen => this;

    private static int ScreenCellWidth => Utils.CanvasExtensions.GetCellWidth(ScreenLineHeight);

    public int Width => Math.Max(1, (window.Size.Width - 10) / ScreenCellWidth);

    public int Height => Math.Max(1, (window.Size.Height - ScreenTop - 4) / ScreenLineHeight);

    public void Begin()
    {
        lock (textLinesLock)
        {
            screenMode = true;
            screenRows = [];
            cursorColumn = cursorRow = 0;
        }

        displayDirty = true;
    }

    public void End()
    {
        lock (textLinesLock)
        {
            screenMode = false;
        }

        screenKeys.Close();
        displayDirty = true;
    }

    public void WriteAt(int column, int row, string text, bool inverse = false)
    {
        lock (textLinesLock)
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
        }

        displayDirty = true;
    }

    public void SetCursor(int column, int row)
    {
        cursorColumn = column;
        cursorRow = row;
        displayDirty = true;
    }

    public Task<ConsoleKeyInfo?> ReadKeyAsync(CancellationToken cancellation)
    {
        return screenKeys.ReadAsync(cancellation);
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

    /// <summary>The bottom line: the prompt being answered, the command prompt, or nothing while a command runs.</summary>
    private string GetInputLine()
    {
        if (pendingAnswer is not null)
        {
            return answerPrompt + answerEditor.Text + "_";
        }

        return runner.IsRunning ? string.Empty : Prompt + editor.Text + "_";
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
        int columns = Math.Max(1, (window.Size.Width - 10) / Utils.CanvasExtensions.GetCellWidth(LineHeight));

        lock (textLinesLock)
        {
            List<(string Text, Color Color)> rows = GetVisibleRows(rowCount, columns);

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
        }

        window.Flush();
    }

    /// <summary>
    /// The last <paramref name="rowCount"/> screen rows, oldest first: the output lines and then
    /// the input line, each wrapped at <paramref name="columns"/> characters. Wrapping happens
    /// here rather than when a line is added, so a resized window re-wraps everything. A long
    /// input line keeps its end (where the cursor is) on screen.
    /// </summary>
    private List<(string Text, Color Color)> GetVisibleRows(int rowCount, int columns)
    {
        // The line being typed wraps at the column, like a terminal's, so it does not jump around
        // as words grow; output wraps at word boundaries.
        List<string> inputRows = WrapLine(GetInputLine(), columns, keepWords: false);
        if (inputRows.Count > rowCount)
        {
            inputRows.RemoveRange(0, inputRows.Count - rowCount);
        }

        // Collect output rows newest first, only as many as fit above the input line.
        int outputRowCount = rowCount - inputRows.Count;
        List<(string Text, Color Color)> outputRows = [];
        for (int i = outputLines.Count - 1; i >= 0 && outputRows.Count < outputRowCount; i--)
        {
            (string text, Color color) = outputLines[i];
            List<string> wrapped = WrapLine(text, columns, keepWords: true);
            for (int j = wrapped.Count - 1; j >= 0 && outputRows.Count < outputRowCount; j--)
            {
                outputRows.Add((wrapped[j], color));
            }
        }

        List<(string Text, Color Color)> rows = new(rowCount);
        for (int i = outputRows.Count; i < outputRowCount; i++)
        {
            rows.Add((string.Empty, OutputColor));
        }

        for (int i = outputRows.Count - 1; i >= 0; i--)
        {
            rows.Add(outputRows[i]);
        }

        foreach (string inputRow in inputRows)
        {
            rows.Add((inputRow, OutputColor));
        }

        return rows;
    }

    /// <summary>
    /// Splits a line into rows of at most <paramref name="columns"/> characters, also at any
    /// line breaks it contains. With <paramref name="keepWords"/> a row breaks after its last
    /// space so words stay whole, and only a word longer than a row is split. An empty line is
    /// one empty row.
    /// </summary>
    private static List<string> WrapLine(string line, int columns, bool keepWords)
    {
        List<string> rows = [];

        foreach (string part in line.Split('\n'))
        {
            string text = part.TrimEnd('\r');
            if (text.Length == 0)
            {
                rows.Add(string.Empty);
                continue;
            }

            int start = 0;
            while (text.Length - start > columns)
            {
                // Break after the last space that fits, unless the row would then hold no text.
                int length = columns;
                if (keepWords)
                {
                    int space = text.LastIndexOf(' ', start + columns - 1, columns);
                    if (space > start && text[start..space].Trim().Length > 0)
                    {
                        length = space - start + 1;
                    }
                }

                rows.Add(text.Substring(start, length));
                start += length;
            }

            rows.Add(text[start..]);
        }

        return rows;
    }

    /// <summary>Shows the text screen: each row a Text over a background bar for inverse rows, and the cursor bar.</summary>
    private void UpdateScreenDisplay()
    {
        lock (textLinesLock)
        {
            FitScreenBuffer();

            foreach (Text line in textLines)
            {
                line.Content = "";
            }

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
        }

        window.Flush();
    }

    /// <summary>Hides what text screen mode showed, once a program has given the window back.</summary>
    private void HideScreenElements()
    {
        lock (textLinesLock)
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
}
