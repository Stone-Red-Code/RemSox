using RemSox.Kernel.Configuration;
using RemSox.Kernel.Programs;

namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// The registry of commands, and the parsing and dispatch of a command line to a command or a
/// program (<see cref="ProgramRegistry"/>).
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

    /// <summary>Finds a command by name or alias, else a program by name (as a <see cref="ProgramCommand"/>).</summary>
    public static ICommand? Find(string name)
    {
        return commands.GetValueOrDefault(name)
            ?? (ProgramRegistry.Find(name) is { } program ? new ProgramCommand(program) : null);
    }

    /// <summary>
    /// Runs a command line in <paramref name="session"/>, writing to <paramref name="console"/>.
    /// A line whose first word is no command or program but names a script (found as
    /// <see cref="ProgramRegistry.FindFile"/> describes: the current folder, then the
    /// <c>path</c> setting's folders) runs that script with its runner.
    /// </summary>
    /// <param name="addToHistory">Whether the line goes into the shell's history; not for the lines of a script.</param>
    /// <returns>The command's exit code; <see cref="CommandResult.NotFound"/> for an unknown command.</returns>
    public static async Task<int> ExecuteAsync(string line, ShellSession session, ICommandConsole console, CancellationToken cancellation = default, bool addToHistory = true)
    {
        CommandLine? commandLine = CommandLine.Parse(line);
        if (commandLine is null)
        {
            return CommandResult.Success;
        }

        if (addToHistory)
        {
            session.AddHistory(line.Trim());
        }

        ICommand? command = Find(commandLine.Name);
        IReadOnlyList<string> args = commandLine.Arguments;
        string rawArguments = commandLine.RawArguments;

        // Typing a script's name or path runs it, like "hello", "setup.rsh" or "/scripts/hello.ynt".
        string? script = command is null
            ? ProgramRegistry.FindFile(commandLine.Name, session.WorkingDirectory, SystemSettings.GetPathFolders())
            : null;

        if (script is not null)
        {
            command = Find(ProgramRegistry.FindRunner(script)!);
            args = [script, .. commandLine.Arguments];
            rawArguments = commandLine.RawArguments.Length > 0 ? script + " " + commandLine.RawArguments : script;
        }

        if (command is null)
        {
            console.WriteError($"\"{commandLine.Name}\" is not a command, program or script. Type 'help' for a list.");
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
