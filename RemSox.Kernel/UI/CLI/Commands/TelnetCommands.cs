using RemSox.Kernel.Processes;
using RemSox.Kernel.Processing;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class StartTelnetCommand : ICommand
{
    public string Name => "telnet-start";

    public string Description => "Serve this shell over Telnet, unencrypted and without login";

    public string Usage => "telnet-start [port 1-65535, default 23]";

    public int MaxArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        int port = 23;
        if (context.Args.Count == 1 && (!int.TryParse(context.Args[0], out port) || port is <= 0 or > 65535))
        {
            return Task.FromResult(context.UsageError($"Not a port: {context.Args[0]}"));
        }

        if (ProcessManager.IsProcessRunning<TelnetProcess>())
        {
            return Task.FromResult(context.Fail("The Telnet server is already running."));
        }

        _ = ProcessManager.SpawnProcess<TelnetProcess>([port.ToString()]);
        context.Console.WriteLine($"Telnet server started on port {port}. Anyone who can reach it gets a shell.");
        return Task.FromResult(CommandResult.Success);
    }
}

public sealed class StopTelnetCommand : ICommand
{
    public string Name => "telnet-stop";

    public string Description => "Stop the Telnet server (open sessions stay connected until they exit)";

    public int MaxArguments => 0;

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        if (!ProcessManager.IsProcessRunning<TelnetProcess>())
        {
            return context.Fail("The Telnet server is not running.");
        }

        foreach (TelnetProcess process in ProcessManager.GetProcessesOfType<TelnetProcess>())
        {
            await ProcessManager.StopProcessAndWaitAsync(process.Id);
        }

        context.Console.WriteLine("Telnet server stopped.");
        return CommandResult.Success;
    }
}
