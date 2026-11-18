using RemSox.Kernel.Processes;
using RemSox.Kernel.Processing;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class StartGuiCommand : ICommand
{
    public string Name => "start-gui";

    public string Description => "Start the graphical desktop";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        if (ProcessManager.IsProcessRunning<DesktopProcess>())
        {
            return Task.FromResult(context.Fail("The desktop is already running."));
        }

        context.Console.WriteLine("Starting the desktop...");
        _ = ProcessManager.SpawnProcess<DesktopProcess>();
        return Task.FromResult(CommandResult.Success);
    }
}
