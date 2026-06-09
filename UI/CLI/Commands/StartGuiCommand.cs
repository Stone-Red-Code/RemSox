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

        if (ProcessManager.IsProcessRunning<DesktopProcess>())
        {
            printLine("Desktop Process is already running.");
            return;
        }

        foreach (Process process in ProcessManager.GetProcessesOfType<CliProcess>())
        {
            ProcessManager.StopProcess(process.Id);
        }

        _ = ProcessManager.SpawnProcess<DesktopProcess>();
    }
}
