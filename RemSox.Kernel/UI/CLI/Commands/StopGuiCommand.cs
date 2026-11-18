using RemSox.Kernel.Processes;
using RemSox.Kernel.Processing;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class StopGuiCommand : ICommand
{
    public string Name => "stop-gui";

    public string Description => "Stop the graphical desktop";

    public int MaxArguments => 0;

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        if (!ProcessManager.IsProcessRunning<DesktopProcess>())
        {
            return context.Fail("The desktop is not running.");
        }

        context.Console.WriteLine("Stopping the desktop...");

        foreach (Process process in ProcessManager.GetProcessesOfType<DesktopProcess>())
        {
            await ProcessManager.StopProcessAndWaitAsync(process.Id);
        }

        return CommandResult.Success;
    }
}
