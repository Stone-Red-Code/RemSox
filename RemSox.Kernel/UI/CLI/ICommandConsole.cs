namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// The console a command runs in: line output, errors and questions. Each shell implements it for
/// its own screen and keyboard; one on a character screen also offers <see cref="Screen"/>.
/// </summary>
/// <remarks>
/// Commands run on the <see cref="Processing.MainLoop"/> (code after an await resumes there too),
/// so these are only called from the main loop. A shell whose terminal lives on another thread,
/// like Telnet's, hands the work over itself.
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

    /// <summary>
    /// Like <see cref="ReadLineAsync"/>, but what is typed is not shown, for passwords. A console
    /// that cannot hide its input asks nothing.
    /// </summary>
    /// <returns>The line, or null when cancelled, when the terminal closes, or when this console cannot ask.</returns>
    Task<string?> ReadSecretAsync(string prompt, CancellationToken cancellation)
    {
        return Task.FromResult<string?>(null);
    }

    /// <summary>Gets the screen full-screen programs can take over, or null when this shell has none.</summary>
    ITextScreen? Screen => null;
}
