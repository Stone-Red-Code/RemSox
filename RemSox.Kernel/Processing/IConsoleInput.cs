namespace RemSox.Kernel.Processing;

/// <summary>
/// A process that reads lines the user types, from the shell running it in the foreground.
/// </summary>
public interface IConsoleInput
{
    /// <summary>
    /// Gets or sets how to ask for a line: it shows the prompt and completes with the line, or
    /// with null when the token is cancelled or the terminal closes. When unset, the process has
    /// no terminal to read from. The shell sets it right after starting the process, before its
    /// first tick.
    /// </summary>
    Func<string, CancellationToken, Task<string?>>? ReadLine { get; set; }
}
