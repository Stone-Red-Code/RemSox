using RemSox.Processes;
using RemSox.Processing;

namespace RemSox.UI.CLI.Commands;

public class StopGuiCommand : ICommand
{
    public string Name => "stop-gui";
    public string Description => "Stops the Graphical User Interface (Desktop Process)";

    public async Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        printLine("Stopping Desktop Process...");

        if (!ProcessManager.IsProcessRunning<DesktopProcess>())
        {
            printLine("Desktop Process is not running.");
            return;
        }

        foreach (Process process in ProcessManager.GetProcessesOfType<DesktopProcess>())
        {
            await ProcessManager.StopProcessAndWaitAsync(process.Id);
        }

        _ = ProcessManager.SpawnProcess<CliProcess>();
    }
}
