using RemSox.Processes;
using RemSox.Processing;

namespace RemSox.UI.CLI.Commands;

public class StopRemoteDesktopCommand : ICommand
{
    public string Name => "rdp-stop";
    public string Description => "Stops the remote desktop server";

    public async Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        if (!ProcessManager.IsProcessRunning<RemoteDesktopProcess>())
        {
            printLine("Remote desktop server is not running.");
            return;
        }

        foreach (RemoteDesktopProcess process in ProcessManager.GetProcessesOfType<RemoteDesktopProcess>())
        {
            await ProcessManager.StopProcessAndWaitAsync(process.Id);
        }

        printLine("Remote desktop server stopped.");
    }
}
