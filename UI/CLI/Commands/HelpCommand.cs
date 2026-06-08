using System;

namespace RemSox.UI.GUI.CLI.Commands;

public sealed class HelpCommand : ICommand
{
    public string Name => "help";

    public string Description => "Show this help message";

    public void Execute(string? arguments)
    {
        Console.WriteLine("Available commands:");

        foreach (ICommand command in CommandManager.GetCommands())
        {
            Console.WriteLine($"  {command.Name,-12} - {command.Description}");
        }
    }
}