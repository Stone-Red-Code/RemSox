using System;
using RemSox.Processing;
using RemSox.Processes;
using RemSox.UI.GUI.CLI;

namespace RemSox.UI.GUI.CLI.Commands;

public class StartGuiCommand : ICommand
{
    public string Name => "start-gui";
    public string Description => "Starts the Graphical User Interface (Desktop Process)";

    public void Execute(string? args)
    {
        Console.WriteLine("Starting Desktop Process...");
        ProcessManager.SpawnProcess<DesktopProcess>();
    }
}
