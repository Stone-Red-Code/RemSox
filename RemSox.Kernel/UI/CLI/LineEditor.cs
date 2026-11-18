namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// The line being typed at a prompt, with recall from its shell's history. Each front end maps
/// its own key events onto these operations and redraws <see cref="Text"/>.
/// </summary>
public sealed class LineEditor(ShellSession session)
{
    private int historyIndex = session.HistoryCount;

    public string Text { get; private set; } = string.Empty;

    public void Insert(char c)
    {
        if (!char.IsControl(c))
        {
            Text += c;
        }
    }

    public void Backspace()
    {
        if (Text.Length > 0)
        {
            Text = Text[..^1];
        }
    }

    /// <summary>Replaces the line with the previous history entry.</summary>
    public void HistoryPrevious()
    {
        int count = session.HistoryCount;
        if (count > 0)
        {
            historyIndex = Math.Clamp(historyIndex - 1, 0, count - 1);
            Text = session.GetHistory(historyIndex);
        }
    }

    /// <summary>Replaces the line with the next history entry, or clears it past the newest one.</summary>
    public void HistoryNext()
    {
        int count = session.HistoryCount;
        if (historyIndex >= count - 1)
        {
            historyIndex = count;
            Text = string.Empty;
            return;
        }

        historyIndex++;
        Text = session.GetHistory(historyIndex);
    }

    /// <summary>Returns the line and starts a new, empty one.</summary>
    public string Submit()
    {
        string line = Text;
        Text = string.Empty;

        // The command is added to the history when it runs; recall starts after it.
        historyIndex = session.HistoryCount + (string.IsNullOrWhiteSpace(line) ? 0 : 1);
        return line;
    }

    /// <summary>Clears the line without submitting it.</summary>
    public void Reset()
    {
        Text = string.Empty;
        historyIndex = session.HistoryCount;
    }
}
