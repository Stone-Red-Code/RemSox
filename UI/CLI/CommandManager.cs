using System;

namespace RemSox.UI.GUI.CLI;

public static class CommandManager
{
    private static readonly Dictionary<string, ICommand> commands = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterCommand(ICommand command)
    {
        commands[command.Name] = command;
    }

    public static void RegisterCommands(IEnumerable<ICommand> commandsToRegister)
    {
        foreach (ICommand command in commandsToRegister)
        {
            RegisterCommand(command);
        }
    }

    public static IEnumerable<ICommand> GetCommands()
    {
        return commands.Values.OrderBy(command => command.Name);
    }

    public static bool TryExecute(string input, Action<string> printLine)
    {
        string trimmedInput = input.Trim();

        if (trimmedInput.Length == 0)
        {
            return false;
        }

        foreach (KeyValuePair<string, ICommand> entry in commands
                     .OrderByDescending(entry => entry.Key.Length))
        {
            string commandName = entry.Key;

            if (!trimmedInput.Equals(commandName, StringComparison.OrdinalIgnoreCase) &&
                !trimmedInput.StartsWith(commandName + " ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? arguments = null;

            if (trimmedInput.Length > commandName.Length)
            {
                arguments = trimmedInput.Substring(commandName.Length).TrimStart();
            }

            entry.Value.Execute(arguments, printLine);
            return true;
        }

        return false;
    }
}