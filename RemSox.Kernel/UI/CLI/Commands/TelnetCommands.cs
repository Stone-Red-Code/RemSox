using RemSox.Kernel.Processes;
using RemSox.Kernel.Processing;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class StartTelnetCommand : ICommand
{
    public string Name => "telnet-start";

    public string Description => "Serves this shell over Telnet, unencrypted and without login (telnet-start [port], default 23)";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        int port = 23;
        if (!string.IsNullOrWhiteSpace(arguments) && (!int.TryParse(arguments.Trim(), out port) || port <= 0 || port > 65535))
        {
            printLine("Usage: telnet-start [port] (1-65535)");
            return Task.CompletedTask;
        }

        if (ProcessManager.IsProcessRunning<TelnetProcess>())
        {
            printLine("Telnet server is already running.");
            return Task.CompletedTask;
        }

        _ = ProcessManager.SpawnProcess<TelnetProcess>([port.ToString()]);
        printLine($"Telnet server started on port {port}. Anyone who can reach it gets a shell.");
        return Task.CompletedTask;
    }
}

public sealed class StopTelnetCommand : ICommand
{
    public string Name => "telnet-stop";

    public string Description => "Stops the Telnet server (open sessions stay connected until they exit)";

    public async Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        if (!ProcessManager.IsProcessRunning<TelnetProcess>())
        {
            printLine("Telnet server is not running.");
            return;
        }

        foreach (TelnetProcess process in ProcessManager.GetProcessesOfType<TelnetProcess>())
        {
            await ProcessManager.StopProcessAndWaitAsync(process.Id);
        }

        printLine("Telnet server stopped.");
    }
}
