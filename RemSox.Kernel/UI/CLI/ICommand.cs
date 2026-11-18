namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// A command executable from any shell: the local CLI, the GUI Terminal or a Telnet session.
/// </summary>
/// <remarks>
/// One instance serves every shell, possibly at the same time, so a command keeps no state of
/// its own between or during runs: everything a run needs is in its <see cref="CommandContext"/>.
/// </remarks>
public interface ICommand
{
    /// <summary>Gets the name the command is invoked by.</summary>
    string Name { get; }

    /// <summary>Gets a one-line description, shown by <c>help</c>.</summary>
    string Description { get; }

    /// <summary>
    /// Gets the invocation syntax, such as <c>cp &lt;source&gt; &lt;destination&gt;</c>, shown by
    /// <c>help &lt;command&gt;</c> and whenever the arguments are wrong.
    /// </summary>
    string Usage => Name;

    /// <summary>Gets other names the command can be invoked by.</summary>
    IReadOnlyList<string> Aliases => [];

    /// <summary>Gets the fewest arguments the command accepts; fewer prints the usage.</summary>
    int MinArguments => 0;

    /// <summary>Gets the most arguments the command accepts; more prints the usage.</summary>
    int MaxArguments => int.MaxValue;

    /// <summary>
    /// Runs the command. It should stop soon after <see cref="CommandContext.Cancellation"/> is
    /// cancelled (Ctrl+C).
    /// </summary>
    /// <returns>The exit code: one of <see cref="CommandResult"/>'s values.</returns>
    Task<int> ExecuteAsync(CommandContext context);
}

/// <summary>Exit codes of commands, following the shell conventions.</summary>
public static class CommandResult
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int UsageError = 2;
    public const int NotFound = 127;
    public const int Cancelled = 130;
}
