namespace RemSox.UI.GUI.CLI;

using System;

public interface ICommand
{
    string Name { get; }

    string Description { get; }

    void Execute(string? arguments, Action<string> printLine);
}
