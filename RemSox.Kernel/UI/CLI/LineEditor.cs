namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// The line being typed at a command prompt, with history recall from <see cref="CommandManager"/>.
/// Each front end maps its own key events onto these operations and redraws <see cref="Text"/>.
/// </summary>
public sealed class LineEditor
{
    private int historyIndex = CommandManager.GetCommandHistory().Count;

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
        IList<string> history = CommandManager.GetCommandHistory();
        if (history.Count > 0)
        {
            historyIndex = Math.Clamp(historyIndex - 1, 0, history.Count - 1);
            Text = history[historyIndex];
        }
    }

    /// <summary>Replaces the line with the next history entry, or clears it past the newest one.</summary>
    public void HistoryNext()
    {
        IList<string> history = CommandManager.GetCommandHistory();
        if (historyIndex >= history.Count - 1)
        {
            historyIndex = history.Count;
            Text = string.Empty;
            return;
        }

        historyIndex++;
        Text = history[historyIndex];
    }

    /// <summary>Returns the line and starts a new, empty one.</summary>
    public string Submit()
    {
        string line = Text;
        Text = string.Empty;

        // The command is added to the history when it runs; recall starts after it.
        historyIndex = CommandManager.GetCommandHistory().Count + (string.IsNullOrWhiteSpace(line) ? 0 : 1);
        return line;
    }
}
