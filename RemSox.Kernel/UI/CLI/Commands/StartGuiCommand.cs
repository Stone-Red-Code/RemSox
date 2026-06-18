using RemSox.Kernel.Processes;
using RemSox.Kernel.Processing;

namespace RemSox.Kernel.UI.CLI.Commands;

public class StartGuiCommand : ICommand
{
    public string Name => "start-gui";
    public string Description => "Starts the Graphical User Interface (Desktop Process)";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        printLine("Starting Desktop Process...");

        if (ProcessManager.IsProcessRunning<DesktopProcess>())
        {
            printLine("Desktop Process is already running.");
            return Task.CompletedTask;
        }

        _ = ProcessManager.SpawnProcess<DesktopProcess>();
        return Task.CompletedTask;
    }
}
