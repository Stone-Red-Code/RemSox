using RemSox.Kernel.Processes;
using RemSox.Kernel.Processing;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class StartRemoteDesktopCommand : ICommand
{
    public string Name => "rdp-start";

    public string Description => "Start the remote desktop server";

    public string Usage => "rdp-start <port 1-65535>";

    public int MinArguments => 1;

    public int MaxArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        if (!int.TryParse(context.Args[0], out int port) || port is <= 0 or > 65535)
        {
            return Task.FromResult(context.UsageError($"Not a port: {context.Args[0]}"));
        }

        if (ProcessManager.IsProcessRunning<RemoteDesktopProcess>())
        {
            return Task.FromResult(context.Fail("The remote desktop server is already running."));
        }

        _ = ProcessManager.SpawnProcess<RemoteDesktopProcess>([port.ToString()]);
        context.Console.WriteLine($"Remote desktop server started on port {port}.");
        return Task.FromResult(CommandResult.Success);
    }
}
