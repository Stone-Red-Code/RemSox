namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// The console a command runs in: line output, errors and questions. Each shell implements it for
/// its own screen and keyboard.
/// </summary>
/// <remarks>
/// A command may call these from any thread (code after an await resumes on another one), so
/// implementations must be safe to call from any thread.
/// </remarks>
public interface ICommandConsole
{
    /// <summary>Writes a line of output.</summary>
    void WriteLine(string text);

    /// <summary>Writes a line reporting an error, set apart from ordinary output (red, where the terminal has colors).</summary>
    void WriteError(string text);

    /// <summary>Clears the terminal.</summary>
    void Clear();

    /// <summary>
    /// Shows <paramref name="prompt"/> and waits for the user to enter a line.
    /// </summary>
    /// <returns>The line, or null when <paramref name="cancellation"/> is cancelled or the terminal closes.</returns>
    Task<string?> ReadLineAsync(string prompt, CancellationToken cancellation);
}
