namespace RemSox.Kernel.Processing;

/// <summary>
/// A process that writes text, which a shell running it in the foreground shows as it comes.
/// </summary>
public interface IConsoleOutput
{
    /// <summary>
    /// Gets or sets where the output goes, line by line. When unset, it goes to the process log.
    /// The shell sets it right after starting the process, before its first tick.
    /// </summary>
    Action<string>? Output { get; set; }

    /// <summary>
    /// Gets or sets how to clear the terminal the output goes to. Unset when the output goes to
    /// the process log, which keeps what was written. The shell sets it with <see cref="Output"/>.
    /// </summary>
    Action? Clear { get; set; }
}
