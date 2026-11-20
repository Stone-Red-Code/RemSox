namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// A grid of character cells a command can take over, for full-screen programs such as <c>edit</c>.
/// Offered by shells that show text in cells (the local CLI, Telnet sessions and Terminal windows)
/// through <see cref="ICommandConsole.Screen"/>.
/// </summary>
/// <remarks>
/// Between <see cref="Begin"/> and <see cref="End"/> the program owns the screen and every key,
/// Ctrl+C included. Members are called on the main loop, like the rest of a command.
/// </remarks>
public interface ITextScreen
{
    /// <summary>Gets the number of columns.</summary>
    int Width { get; }

    /// <summary>Gets the number of rows.</summary>
    int Height { get; }

    /// <summary>Takes over the screen and the keyboard, and clears the screen.</summary>
    void Begin();

    /// <summary>Clears the screen and gives it and the keyboard back to the shell.</summary>
    void End();

    /// <summary>Writes <paramref name="text"/> at a position, without moving past the end of the row.</summary>
    void WriteAt(int column, int row, string text, bool inverse = false);

    /// <summary>Places the cursor.</summary>
    void SetCursor(int column, int row);

    /// <summary>Waits for the next key; null when <paramref name="cancellation"/> is cancelled or the terminal closes.</summary>
    Task<ConsoleKeyInfo?> ReadKeyAsync(CancellationToken cancellation);
}
