using RemSox.Processes;
using RemSox.Processing;

namespace RemSox.UI.CLI.Commands;

public class StartGuiCommand : ICommand
{
    public string Name => "start-gui";
    public string Description => "Starts the Graphical User Interface (Desktop Process)";

    public void Execute(string? arguments, Action<string> printLine)
    {
        printLine("Starting Desktop Process...");
        _ = ProcessManager.SpawnProcess<DesktopProcess>();
    }
}
