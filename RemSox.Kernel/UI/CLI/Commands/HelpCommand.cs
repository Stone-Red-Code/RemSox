namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class HelpCommand : ICommand
{
    public string Name => "help";

    public string Description => "Show this help message";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        printLine("Available commands:");

        foreach (ICommand command in CommandManager.GetCommands())
        {
            printLine($"  {command.Name,-12} - {command.Description}");
        }

        return Task.CompletedTask;
    }
}