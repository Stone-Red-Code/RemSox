using RemSox.Kernel.Accounts;
using RemSox.Kernel.Programs;

namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// The registry of commands (the shell's own, like <c>cmd</c>'s internal commands), and the
/// parsing and dispatch of a command line: to a command, else to what <see cref="Launcher"/>
/// resolves the name to (a program or a file).
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

    /// <summary>Finds a command by name or alias; programs are not commands (see <see cref="Launcher"/>).</summary>
    public static ICommand? Find(string name)
    {
        return commands.GetValueOrDefault(name);
    }

    /// <summary>
    /// Runs a command line in <paramref name="session"/>, writing to <paramref name="console"/>.
    /// A first word that is no command is resolved by <see cref="Launcher"/>: a program starts,
    /// a script runs in this shell, a shortcut starts its target, any other file opens with its
    /// associated command or program.
    /// </summary>
    /// <param name="addToHistory">Whether the line goes into the shell's history; not for the lines of a script.</param>
    /// <param name="elevated">Whether the line runs with admin rights (<c>sudo</c>); a script or program it starts too.</param>
    /// <returns>The command's exit code; <see cref="CommandResult.NotFound"/> for an unknown command.</returns>
    public static async Task<int> ExecuteAsync(string line, ShellSession session, ICommandConsole console, CancellationToken cancellation = default, bool addToHistory = true, bool elevated = false)
    {
        Caller caller = new(session.Logon, elevated);

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

        if (command is null)
        {
            switch (Launcher.Resolve(commandLine.Name, args, session.WorkingDirectory, session.Account))
            {
                case null:
                    break;

                case { Kind: LaunchKind.Script } script:
                    if (args.Count > 0)
                    {
                        console.WriteError("A script takes no arguments.");
                        return CommandResult.UsageError;
                    }

                    // In this shell, so a script's cd changes the directory of the shell that ran it.
                    return await ShellScript.RunAsync(script.Name, session, console, cancellation, elevated);

                case { Kind: LaunchKind.Shortcut } shortcut:
                    return ProgramRunner.Report(Shortcut.StartFile(shortcut.Name, args, session.WorkingDirectory, caller), console);

                case { Program: { } program } target:
                    return await ProgramRunner.RunAsync(program, target.RunArguments, session, caller, console, cancellation);

                case { Kind: LaunchKind.File } file:
                    // The opener command runs as if typed, with the file in front of the arguments.
                    command = Find(file.Command!);
                    args = file.RunArguments;
                    rawArguments = commandLine.RawArguments.Length > 0 ? file.Name + " " + commandLine.RawArguments : file.Name;
                    break;
            }
        }

        if (command is null)
        {
            console.WriteError($"\"{commandLine.Name}\" is not a command, program or file that can be opened. Type 'help' for a list.");
            return CommandResult.NotFound;
        }

        CommandContext context = new(command, args, rawArguments, session, console, cancellation, elevated);

        if (args.Count < command.MinArguments || args.Count > command.MaxArguments)
        {
            return context.UsageError();
        }

        return await command.ExecuteAsync(context);
    }
}
