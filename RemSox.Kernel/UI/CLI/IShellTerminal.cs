namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// Where a shell's text appears: the local console, a Terminal window or a Telnet client. It only
/// shows what <see cref="ShellFrontEnd"/> tells it to; the keys, the line being typed, questions
/// and full-screen programs are handled there, the same for every shell.
/// </summary>
/// <remarks>Called on the main loop, like the rest of the shell.</remarks>
public interface IShellTerminal
{
    /// <summary>Writes a line of output above the input line.</summary>
    void WriteLine(string text);

    /// <summary>Writes a line reporting an error (red, where the terminal has colors).</summary>
    void WriteError(string text);

    /// <summary>Clears the output.</summary>
    void Clear();

    /// <summary>
    /// Shows the line being typed, its prompt included, replacing the one shown before; null hides
    /// it (while a command runs).
    /// </summary>
    void ShowInput(string? line);

    /// <summary>Keeps the input line shown as output, as it was entered, and starts an empty one below it.</summary>
    void CommitInput();

    /// <summary>Raised on the main loop when <see cref="Width"/> or <see cref="Height"/> changed.</summary>
    event Action? Resized;

    /// <summary>Gets the number of columns of the text screen.</summary>
    int Width { get; }

    /// <summary>Gets the number of rows of the text screen.</summary>
    int Height { get; }

    /// <summary>Clears the terminal and switches it to a text screen for a full-screen program.</summary>
    void BeginScreen();

    /// <summary>Leaves the text screen and goes back to the shell's output, cleared.</summary>
    void EndScreen();

    /// <summary>Writes <paramref name="text"/> on the text screen, without moving past the end of the row.</summary>
    void WriteAt(int column, int row, string text, bool inverse);

    /// <summary>Places the text screen's cursor.</summary>
    void SetCursor(int column, int row);
}
