namespace RemSox.UI.CLI;

public static class CommandManager
{
    private static readonly Dictionary<string, ICommand> commands = new(StringComparer.OrdinalIgnoreCase);

    private static readonly List<string> commandHistory = [];

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

    public static IList<string> GetCommandHistory()
    {
        return commandHistory;
    }

    public static async Task<bool> TryExecute(string input, Action<string> printLine)
    {
        string trimmedInput = input.Trim();

        if (trimmedInput.Length == 0)
        {
            return false;
        }

        if (commandHistory.Count == 0 || commandHistory[^1] != trimmedInput)
        {
            commandHistory.Add(trimmedInput);
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
                arguments = trimmedInput[commandName.Length..].TrimStart();
            }

            await entry.Value.ExecuteAsync(arguments, printLine);
            return true;
        }

        return false;
    }
}