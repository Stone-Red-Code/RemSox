namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// A shell's terminal on <see cref="Console"/>: the local text console, or a Telnet client when
/// used on its session's thread (Console follows the thread).
/// </summary>
/// <remarks>
/// Every write is a redraw (and on Telnet goes over the network), so the input line is updated
/// by writing only what changed.
/// </remarks>
public sealed class ConsoleTerminal : IShellTerminal
{
    // The input line as shown; the cursor is at its end. Empty means the cursor is at the start of a line.
    private string shown = string.Empty;

    public void WriteLine(string text)
    {
        // Output goes above the line being typed: move it out of the way, then show it again below.
        string input = shown;
        EraseInput();
        Console.WriteLine(text);
        ShowInput(input);
    }

    public void WriteError(string text)
    {
        string input = shown;
        EraseInput();

        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;

        ShowInput(input);
    }

    public void Clear()
    {
        Console.Clear();
        shown = string.Empty;
    }

    public void ShowInput(string? line)
    {
        string next = line ?? string.Empty;

        if (next == shown)
        {
            return;
        }

        if (next.StartsWith(shown, StringComparison.Ordinal))
        {
            // Typing: write only the new characters.
            Console.Write(next[shown.Length..]);
        }
        else if (shown.StartsWith(next, StringComparison.Ordinal))
        {
            // Backspace: blank out the removed characters.
            int removed = shown.Length - next.Length;
            Console.Write(new string('\b', removed) + new string(' ', removed) + new string('\b', removed));
        }
        else
        {
            // A history entry or another prompt: rewrite the line, blanking what the old one had more.
            int blank = Math.Max(0, shown.Length - next.Length);
            Console.Write("\r" + next + new string(' ', blank) + new string('\b', blank));
        }

        shown = next;
    }

    public void CommitInput()
    {
        Console.WriteLine();
        shown = string.Empty;
    }

    private void EraseInput()
    {
        if (shown.Length > 0)
        {
            Console.Write("\r" + new string(' ', shown.Length) + "\r");
            shown = string.Empty;
        }
    }

    // --- Text screen ---

    public int Width => Console.WindowWidth;

    public int Height => Console.WindowHeight;

    public void BeginScreen()
    {
        Console.Clear();
        shown = string.Empty;
    }

    public void EndScreen()
    {
        Console.ResetColor();
        Console.Clear();
        shown = string.Empty;
    }

    public void WriteAt(int column, int row, string text, bool inverse)
    {
        // Never the last column: writing it moves the cursor on, and on the last row scrolls.
        int room = Console.WindowWidth - 1 - column;
        if (room <= 0 || row < 0 || row >= Console.WindowHeight)
        {
            return;
        }

        Console.SetCursorPosition(column, row);

        if (inverse)
        {
            Console.ForegroundColor = ConsoleColor.Black;
            Console.BackgroundColor = ConsoleColor.Gray;
        }

        Console.Write(text.Length > room ? text[..room] : text);

        if (inverse)
        {
            Console.ResetColor();
        }
    }

    public void SetCursor(int column, int row)
    {
        Console.SetCursorPosition(Math.Clamp(column, 0, Console.WindowWidth - 1), Math.Clamp(row, 0, Console.WindowHeight - 1));
    }
}
