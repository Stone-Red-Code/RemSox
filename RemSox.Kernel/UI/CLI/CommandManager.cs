namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// The registry of commands, and the parsing and dispatch of a command line to one of them.
/// </summary>
public static class CommandManager
{
    // Commands are registered at boot, before any shell runs, and only read afterwards.
    private static readonly Dictionary<string, ICommand> commands = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<ICommand> registered = [];

    public static void RegisterCommand(ICommand command)
    {
        registered.Add(command);
        commands[command.Name] = command;

        foreach (string alias in command.Aliases)
        {
            commands[alias] = command;
        }
    }

    public static void RegisterCommands(IEnumerable<ICommand> commandsToRegister)
    {
        foreach (ICommand command in commandsToRegister)
        {
            RegisterCommand(command);
        }
    }

    /// <summary>Returns every command once (not once per alias), by name.</summary>
    public static IEnumerable<ICommand> GetCommands()
    {
        return registered.OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Finds a command by name or alias.</summary>
    public static ICommand? Find(string name)
    {
        return commands.GetValueOrDefault(name);
    }

    /// <summary>
    /// Runs a command line in <paramref name="session"/>, writing to <paramref name="console"/>.
    /// A line whose first word is a YesNt script path runs that script.
    /// </summary>
    /// <returns>The command's exit code; <see cref="CommandResult.NotFound"/> for an unknown command.</returns>
    public static async Task<int> ExecuteAsync(string line, ShellSession session, ICommandConsole console, CancellationToken cancellation = default)
    {
        CommandLine? commandLine = CommandLine.Parse(line);
        if (commandLine is null)
        {
            return CommandResult.Success;
        }

        session.AddHistory(line.Trim());

        ICommand? command = Find(commandLine.Name);
        IReadOnlyList<string> args = commandLine.Arguments;
        string rawArguments = commandLine.RawArguments;

        // Typing a script path runs it, like "hello.ynt" or "/mnt/scripts/hello.ynt".
        if (command is null && commandLine.Name.EndsWith(Processes.YesNtInterpreterProcess.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            command = Find("yesnt");
            args = [commandLine.Name, .. commandLine.Arguments];
            rawArguments = line.Trim();
        }

        if (command is null)
        {
            console.WriteError($"\"{commandLine.Name}\" is not a command. Type 'help' for a list.");
            return CommandResult.NotFound;
        }

        CommandContext context = new(command, args, rawArguments, session, console, cancellation);

        if (args.Count < command.MinArguments || args.Count > command.MaxArguments)
        {
            return context.UsageError();
        }

        return await command.ExecuteAsync(context);
    }
}
