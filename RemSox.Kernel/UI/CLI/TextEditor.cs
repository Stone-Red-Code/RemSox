using System.Text;

using RemSox.Kernel.FileSystem;

namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// A small full-screen text editor in the style of nano, on an <see cref="ITextScreen"/>.
/// </summary>
/// <remarks>
/// The screen is a title bar, the text, and a bar for help and messages. Only rows that changed
/// since the last frame are written, since each console write redraws the display on Cosmos and
/// each one is sent over the network on Telnet. Tabs are turned into spaces when a file is read.
/// Long lines are soft-wrapped: shown over several screen rows, broken after a space where one
/// fits, but kept as one line in the file. Up and Down move by screen row, as in nano.
/// </remarks>
internal sealed class TextEditor(ITextScreen screen, string path, CancellationToken cancellation)
{
    private const int TabWidth = 4;
    private const string Help = "^S/^O Save  ^Q/^X Quit  ^K Cut line  ^U Paste line";

    private List<string> lines = [string.Empty];
    private int row, column;            // cursor position in the text
    private int wantedX;                // the screen column up/down movement tries to keep
    private int top, topSegment;        // first line on screen, and which of its wrapped rows
    private bool modified;
    private string? message;
    private string? cutLine;
    private string?[] shown = [];        // the rows as last written, to write only changes

    private int Columns => Math.Max(1, screen.Width - 1);

    private int TextRows => Math.Max(1, screen.Height - 2);

    /// <summary>Runs the editor until the user quits.</summary>
    /// <returns>True when it ended normally, false when the screen went away or Ctrl+C cancelled it.</returns>
    public async Task<bool> RunAsync()
    {
        Load();
        screen.Begin();

        try
        {
            while (true)
            {
                Render();

                if (await screen.ReadKeyAsync(cancellation) is not { } key)
                {
                    return false;
                }

                message = null;

                if (IsControl(key, 'Q') || IsControl(key, 'X'))
                {
                    if (await ConfirmQuitAsync())
                    {
                        return true;
                    }
                }
                else
                {
                    HandleKey(key);
                }
            }
        }
        finally
        {
            screen.End();
        }
    }

    private void Load()
    {
        if (!File.Exists(path))
        {
            message = "New file";
            return;
        }

        lines = [.. File.ReadAllLines(path).Select(line => line.Replace("\t", new string(' ', TabWidth)))];
        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        message = $"Read {lines.Count} line(s)";
    }

    private bool Save()
    {
        try
        {
            SafeFile.WriteAllLines(path, lines);
            modified = false;
            message = $"Saved {lines.Count} line(s) to {path}";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            message = "Not saved: " + ex.Message;
            return false;
        }
    }

    /// <summary>Asks about unsaved changes; true when the editor may close.</summary>
    private async Task<bool> ConfirmQuitAsync()
    {
        if (!modified)
        {
            return true;
        }

        message = "Save changes? (y)es, (n)o, any other key to keep editing";
        Render();

        if (await screen.ReadKeyAsync(cancellation) is not { } answer)
        {
            return true;
        }

        message = null;
        return char.ToLowerInvariant(answer.KeyChar) switch
        {
            'y' => Save(),
            'n' => true,
            _ => false,
        };
    }

    private void HandleKey(ConsoleKeyInfo key)
    {
        string line = lines[row];

        if (IsControl(key, 'S') || IsControl(key, 'O'))
        {
            _ = Save();
            return;
        }

        if (IsControl(key, 'K'))
        {
            CutLine();
            return;
        }

        if (IsControl(key, 'U'))
        {
            PasteLine();
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                if (column > 0)
                {
                    column--;
                }
                else if (row > 0)
                {
                    row--;
                    column = lines[row].Length;
                }
                RememberX();
                return;

            case ConsoleKey.RightArrow:
                if (column < line.Length)
                {
                    column++;
                }
                else if (row < lines.Count - 1)
                {
                    row++;
                    column = 0;
                }
                RememberX();
                return;

            case ConsoleKey.UpArrow:
                MoveScreenRows(-1);
                return;

            case ConsoleKey.DownArrow:
                MoveScreenRows(1);
                return;

            case ConsoleKey.PageUp:
                MoveScreenRows(-TextRows);
                return;

            case ConsoleKey.PageDown:
                MoveScreenRows(TextRows);
                return;

            case ConsoleKey.Home:
                column = 0;
                RememberX();
                return;

            case ConsoleKey.End:
                column = line.Length;
                RememberX();
                return;

            case ConsoleKey.Enter:
                lines[row] = line[..column];
                lines.Insert(row + 1, line[column..]);
                row++;
                column = wantedX = 0;
                modified = true;
                return;

            case ConsoleKey.Backspace:
                if (column > 0)
                {
                    lines[row] = line.Remove(column - 1, 1);
                    column--;
                }
                else if (row > 0)
                {
                    column = lines[row - 1].Length;
                    lines[row - 1] += line;
                    lines.RemoveAt(row);
                    row--;
                }
                else
                {
                    return;
                }
                RememberX();
                modified = true;
                return;

            case ConsoleKey.Delete:
                if (column < line.Length)
                {
                    lines[row] = line.Remove(column, 1);
                }
                else if (row < lines.Count - 1)
                {
                    lines[row] = line + lines[row + 1];
                    lines.RemoveAt(row + 1);
                }
                else
                {
                    return;
                }
                modified = true;
                return;

            case ConsoleKey.Tab:
                Insert(new string(' ', TabWidth - (column % TabWidth)));
                return;
        }

        // A Ctrl combination the editor does not use types nothing (a window passes Ctrl+A as 'a').
        if (!char.IsControl(key.KeyChar) && !key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            Insert(key.KeyChar.ToString());
        }
    }

    private void Insert(string text)
    {
        lines[row] = lines[row].Insert(column, text);
        column += text.Length;
        RememberX();
        modified = true;
    }

    /// <summary>Remembers the cursor's screen column, for Up/Down to return to.</summary>
    private void RememberX()
    {
        List<int> starts = SegmentStarts(lines[row], Columns);
        wantedX = column - starts[SegmentOf(starts, column)];
    }

    /// <summary>
    /// Moves the cursor <paramref name="delta"/> screen rows up or down, through the wrapped rows
    /// of long lines, keeping to the remembered screen column where the row is long enough.
    /// </summary>
    private void MoveScreenRows(int delta)
    {
        int columns = Columns;
        List<int> starts = SegmentStarts(lines[row], columns);
        int segment = SegmentOf(starts, column);

        for (; delta > 0; delta--)
        {
            if (segment + 1 < starts.Count)
            {
                segment++;
            }
            else if (row < lines.Count - 1)
            {
                row++;
                starts = SegmentStarts(lines[row], columns);
                segment = 0;
            }
            else
            {
                break;
            }
        }

        for (; delta < 0; delta++)
        {
            if (segment > 0)
            {
                segment--;
            }
            else if (row > 0)
            {
                row--;
                starts = SegmentStarts(lines[row], columns);
                segment = starts.Count - 1;
            }
            else
            {
                break;
            }
        }

        column = ColumnAt(lines[row], starts, segment, wantedX);
    }

    /// <summary>
    /// The text column at screen column <paramref name="x"/> of a wrapped row, kept on that row:
    /// at most its last character, or the line's end on the last row.
    /// </summary>
    private static int ColumnAt(string line, List<int> starts, int segment, int x)
    {
        int end = segment + 1 < starts.Count ? starts[segment + 1] - 1 : line.Length;
        return Math.Min(starts[segment] + x, end);
    }

    private void CutLine()
    {
        cutLine = lines[row];

        if (lines.Count == 1)
        {
            lines[0] = string.Empty;
        }
        else
        {
            lines.RemoveAt(row);
            row = Math.Min(row, lines.Count - 1);
        }

        column = ColumnAt(lines[row], SegmentStarts(lines[row], Columns), 0, wantedX);
        modified = true;
        message = "Cut the line (^U pastes it)";
    }

    private void PasteLine()
    {
        if (cutLine is null)
        {
            message = "Nothing cut yet (^K cuts a line)";
            return;
        }

        lines.Insert(row, cutLine);
        row++;
        column = ColumnAt(lines[row], SegmentStarts(lines[row], Columns), 0, wantedX);
        modified = true;
    }

    /// <summary>True for Ctrl+<paramref name="letter"/>, as a key with Control or as its control character.</summary>
    private static bool IsControl(ConsoleKeyInfo key, char letter)
    {
        return key.KeyChar == (char)(letter - 'A' + 1)
            || (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == (ConsoleKey)letter);
    }

    // --- Drawing ---

    private void Render()
    {
        int columns = Columns;
        int textRows = TextRows;
        int height = textRows + 2;

        if (shown.Length != height)
        {
            shown = new string?[height];
        }

        List<int> cursorStarts = SegmentStarts(lines[row], columns);
        int cursorSegment = SegmentOf(cursorStarts, column);
        ScrollToCursor(cursorSegment, columns, textRows);

        string name = Path.GetFileName(path) + (modified ? " *" : string.Empty);
        string position = $"Ln {row + 1}, Col {column + 1} ";
        Show(0, Fit($" edit  {name}", columns - position.Length) + position, columns, inverse: true);

        // Fill the text rows from the top line's wrapped row on, line after line.
        int lineIndex = top;
        int segment = topSegment;
        List<int>? starts = null;
        int cursorScreenRow = 0;

        for (int i = 0; i < textRows; i++)
        {
            if (lineIndex >= lines.Count)
            {
                Show(i + 1, "~", columns, inverse: false);
                continue;
            }

            string line = lines[lineIndex];
            starts ??= SegmentStarts(line, columns);
            int start = starts[segment];
            int end = segment + 1 < starts.Count ? starts[segment + 1] : line.Length;

            if (lineIndex == row && segment == cursorSegment)
            {
                cursorScreenRow = i;
            }

            Show(i + 1, line[start..end], columns, inverse: false);

            if (++segment >= starts.Count)
            {
                lineIndex++;
                segment = 0;
                starts = null;
            }
        }

        Show(height - 1, " " + (message ?? Help), columns, inverse: true);
        screen.SetCursor(column - cursorStarts[cursorSegment], cursorScreenRow + 1);
    }

    /// <summary>
    /// Scrolls so the cursor's wrapped row is among the <paramref name="textRows"/> shown: up to
    /// it when it is above the screen, or until it is the last row when it is below.
    /// </summary>
    private void ScrollToCursor(int cursorSegment, int columns, int textRows)
    {
        // Lines may have been removed or shortened since the last frame.
        top = Math.Clamp(top, 0, lines.Count - 1);
        topSegment = Math.Clamp(topSegment, 0, SegmentStarts(lines[top], columns).Count - 1);

        if (row < top || (row == top && cursorSegment < topSegment))
        {
            top = row;
            topSegment = cursorSegment;
            return;
        }

        // Count the screen rows from the top to the cursor, stopping once they are a screenful.
        int distance = 0;
        int line = top;
        int segment = topSegment;
        while (line < row && distance < textRows)
        {
            distance += SegmentStarts(lines[line], columns).Count - segment;
            segment = 0;
            line++;
        }

        if (line == row && distance + cursorSegment - segment < textRows)
        {
            return;
        }

        // Below the screen: make the cursor's row the last one shown.
        top = row;
        topSegment = cursorSegment;
        for (int back = textRows - 1; back > 0; back--)
        {
            if (topSegment > 0)
            {
                topSegment--;
            }
            else if (top > 0)
            {
                top--;
                topSegment = SegmentStarts(lines[top], columns).Count - 1;
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>
    /// Where each wrapped screen row of a line starts. A row of at most
    /// <paramref name="columns"/> characters breaks after its last space so words stay whole;
    /// a word longer than a row is split. Every line, even an empty one, has a first row at 0.
    /// </summary>
    private static List<int> SegmentStarts(string line, int columns)
    {
        List<int> starts = [0];
        int start = 0;

        while (line.Length - start > columns)
        {
            int length = columns;
            int space = line.LastIndexOf(' ', start + columns - 1, columns);
            if (space > start && line[start..space].Trim().Length > 0)
            {
                length = space - start + 1;
            }

            start += length;
            starts.Add(start);
        }

        return starts;
    }

    /// <summary>The wrapped row a text column is on: the last one starting at or before it.</summary>
    private static int SegmentOf(List<int> starts, int column)
    {
        int segment = 0;
        while (segment + 1 < starts.Count && starts[segment + 1] <= column)
        {
            segment++;
        }

        return segment;
    }

    /// <summary>Writes a row padded to the width, unless it shows that already.</summary>
    private void Show(int screenRow, string text, int columns, bool inverse)
    {
        string padded = Fit(text, columns);
        string key = (inverse ? "\u0001" : string.Empty) + padded;

        if (shown[screenRow] != key)
        {
            screen.WriteAt(0, screenRow, padded, inverse);
            shown[screenRow] = key;
        }
    }

    private static string Fit(string text, int columns)
    {
        if (text.Length >= columns)
        {
            return text[..columns];
        }

        return new StringBuilder(text, columns).Append(' ', columns - text.Length).ToString();
    }
}
