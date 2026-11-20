using RemSox.Kernel.Logging;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class ShutdownCommand : ICommand
{
    public string Name => "shutdown";

    public string Description => "Shut down the system";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        PowerCommands.Prepare(context, "Shutting down", "shutdown");
        Sys.Power.Shutdown();
        return Task.FromResult(CommandResult.Success);
    }
}

public sealed class RebootCommand : ICommand
{
    public string Name => "reboot";

    public string Description => "Reboot the system";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        PowerCommands.Prepare(context, "Rebooting", "reboot");
        Sys.Power.Reboot();
        return Task.FromResult(CommandResult.Success);
    }
}

internal static class PowerCommands
{
    /// <summary>Says what is about to happen and writes the log to disk, which the power-off would otherwise lose.</summary>
    public static void Prepare(CommandContext context, string doing, string what)
    {
        context.Console.WriteLine(doing + " system...");
        SystemLog.Log($"System {what} requested.", LogSeverity.Info);
        SystemLog.Flush();
    }
}
