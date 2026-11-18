using RemSox.Kernel.Processes;
using RemSox.Kernel.Processing;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class StopRemoteDesktopCommand : ICommand
{
    public string Name => "rdp-stop";

    public string Description => "Stop the remote desktop server";

    public int MaxArguments => 0;

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        if (!ProcessManager.IsProcessRunning<RemoteDesktopProcess>())
        {
            return context.Fail("The remote desktop server is not running.");
        }

        foreach (RemoteDesktopProcess process in ProcessManager.GetProcessesOfType<RemoteDesktopProcess>())
        {
            await ProcessManager.StopProcessAndWaitAsync(process.Id);
        }

        context.Output.WriteLine("Remote desktop server stopped.");
        return CommandResult.Success;
    }
}
