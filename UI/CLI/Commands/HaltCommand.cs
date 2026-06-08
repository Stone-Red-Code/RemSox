using System;

namespace RemSox.UI.GUI.CLI.Commands;

public sealed class HaltCommand : ICommand
{
    public string Name => "halt";

    public string Description => "Halt the system";

    public void Execute(string? arguments)
    {
        Console.WriteLine("Halting system...");
        Environment.Exit(0);
    }
}