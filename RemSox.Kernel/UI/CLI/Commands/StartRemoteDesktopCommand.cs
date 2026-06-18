using RemSox.Kernel.Processes;
using RemSox.Kernel.Processing;

namespace RemSox.Kernel.UI.CLI.Commands;

public class StartRemoteDesktopCommand : ICommand
{
    public string Name => "rdp-start";
    public string Description => "Starts the remote desktop server on the specified port (rdp-start <port>)";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        if (string.IsNullOrWhiteSpace(arguments) || !int.TryParse(arguments.Trim(), out int port) || port <= 0 || port > 65535)
        {
            printLine("Usage: rdp-start <port> (1-65535)");
            return Task.CompletedTask;
        }

        if (ProcessManager.IsProcessRunning<RemoteDesktopProcess>())
        {
            printLine("Remote desktop server is already running.");
            return Task.CompletedTask;
        }

        _ = ProcessManager.SpawnProcess<RemoteDesktopProcess>([port.ToString()]);
        printLine($"Remote desktop server started on port {port}.");
        return Task.CompletedTask;
    }
}
