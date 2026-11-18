using RemSox.Kernel.Logging;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class ShutdownCommand : ICommand
{
    public string Name => "shutdown";

    public string Description => "Shut down the system";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        context.Console.WriteLine("Shutting down system...");
        SystemLog.Log("System shutdown requested.", LogSeverity.Info);
        SystemLog.Flush();
        Sys.Power.Shutdown();
        return Task.FromResult(CommandResult.Success);
    }
}
