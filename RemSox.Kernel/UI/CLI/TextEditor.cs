using System.Text;

namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// A small full-screen text editor in the style of nano, on an <see cref="ITextScreen"/>.
/// </summary>
/// <remarks>
/// The screen is a title bar, the text, and a bar for help and messages. Only rows that changed
/// since the last frame are written, since each console write redraws the display on Cosmos and
/// each one is sent over the network on Telnet. Tabs are turned into spaces when a file is read.
/// </remarks>
internal sealed class TextEditor(ITextScreen screen, string path, CancellationToken cancellation)
{
    private const int TabWidth = 4;
    private const string Help = "^S/^O Save  ^Q/^X Quit  ^K Cut line  ^U Paste line";

    private List<string> lines = [string.Empty];
    private int row, column;            // cursor position in the text
    private int wantedColumn;           // the column up/down movement tries to keep
    private int top, left;              // first text row and column on screen
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
            File.WriteAllLines(path, lines);
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
                wantedColumn = column;
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
                wantedColumn = column;
                return;

            case ConsoleKey.UpArrow:
                MoveRows(-1);
                return;

            case ConsoleKey.DownArrow:
                MoveRows(1);
                return;

            case ConsoleKey.PageUp:
                MoveRows(-TextRows);
                return;

            case ConsoleKey.PageDown:
                MoveRows(TextRows);
                return;

            case ConsoleKey.Home:
                column = wantedColumn = 0;
                return;

            case ConsoleKey.End:
                column = wantedColumn = line.Length;
                return;

            case ConsoleKey.Enter:
                lines[row] = line[..column];
                lines.Insert(row + 1, line[column..]);
                row++;
                column = wantedColumn = 0;
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
                wantedColumn = column;
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
        wantedColumn = column;
        modified = true;
    }

    private void MoveRows(int delta)
    {
        row = Math.Clamp(row + delta, 0, lines.Count - 1);
        column = Math.Min(wantedColumn, lines[row].Length);
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

        column = Math.Min(wantedColumn, lines[row].Length);
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
        column = Math.Min(wantedColumn, lines[row].Length);
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

        // Scroll so the cursor is on screen.
        top = Math.Clamp(top, Math.Max(0, row - textRows + 1), row);
        left = Math.Clamp(left, Math.Max(0, column - columns + 1), column);

        string name = Path.GetFileName(path) + (modified ? " *" : string.Empty);
        string position = $"Ln {row + 1}, Col {column + 1} ";
        Show(0, Fit($" edit  {name}", columns - position.Length) + position, columns, inverse: true);

        for (int i = 0; i < textRows; i++)
        {
            int lineIndex = top + i;
            string text = lineIndex < lines.Count
                ? (left < lines[lineIndex].Length ? lines[lineIndex][left..] : string.Empty)
                : "~";
            Show(i + 1, text, columns, inverse: false);
        }

        Show(height - 1, " " + (message ?? Help), columns, inverse: true);
        screen.SetCursor(column - left, row - top + 1);
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
