namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// Everything one run of a command works with: its arguments, the shell it runs in, where its
/// output goes and the token that Ctrl+C cancels.
/// </summary>
public sealed class CommandContext(
    ICommand command,
    IReadOnlyList<string> args,
    string rawArguments,
    ShellSession session,
    ICommandConsole console,
    CancellationToken cancellation)
{
    public ICommand Command { get; } = command;

    /// <summary>Gets the arguments, split once by <see cref="CommandLine"/> (quotes grouped, escapes applied).</summary>
    public IReadOnlyList<string> Args { get; } = args;

    /// <summary>Gets the text after the command name as typed, for commands that take free text.</summary>
    public string RawArguments { get; } = rawArguments;

    public ShellSession Session { get; } = session;

    public ICommandConsole Console { get; } = console;

    /// <summary>Gets the token cancelled when the user presses Ctrl+C in this shell.</summary>
    public CancellationToken Cancellation { get; } = cancellation;

    /// <summary>Resolves a path against this shell's working directory.</summary>
    public string ResolvePath(string path)
    {
        return Session.ResolvePath(path);
    }

    /// <summary>Asks the user for a line; null when cancelled.</summary>
    public Task<string?> ReadLineAsync(string prompt)
    {
        return Console.ReadLineAsync(prompt, Cancellation);
    }

    /// <summary>Asks a yes/no question; anything but "y" or "yes" is no.</summary>
    public async Task<bool> ConfirmAsync(string question)
    {
        string? answer = await ReadLineAsync(question + " (y/n) ");
        return answer?.Trim().ToLowerInvariant() is "y" or "yes";
    }

    /// <summary>Prints an optional message and the command's usage.</summary>
    /// <returns><see cref="CommandResult.UsageError"/>, to return from the command.</returns>
    public int UsageError(string? message = null)
    {
        if (message is not null)
        {
            Console.WriteError(message);
        }

        Console.WriteError("Usage: " + Command.Usage);
        return CommandResult.UsageError;
    }

    /// <summary>Prints an error.</summary>
    /// <returns><see cref="CommandResult.Failure"/>, to return from the command.</returns>
    public int Fail(string message)
    {
        Console.WriteError(message);
        return CommandResult.Failure;
    }
}
