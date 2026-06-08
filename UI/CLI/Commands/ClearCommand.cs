using System;

namespace RemSox.UI.GUI.CLI.Commands;

public sealed class ClearCommand : ICommand
{
    public string Name => "clear";

    public string Description => "Clear the screen";

    public void Execute(string? arguments)
    {
        Console.Clear();
    }
}