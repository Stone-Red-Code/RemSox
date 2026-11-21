namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class ShutdownCommand : ICommand
{
    public string Name => "shutdown";

    public string Description => "Shut down the system";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        if (MachinePower.Deny(context.Caller, "shut down") is { } denied)
        {
            return Task.FromResult(context.Fail(denied));
        }

        context.Console.WriteLine("Shutting down system...");
        MachinePower.Shutdown();
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
        if (MachinePower.Deny(context.Caller, "reboot") is { } denied)
        {
            return Task.FromResult(context.Fail(denied));
        }

        context.Console.WriteLine("Rebooting system...");
        MachinePower.Reboot();
        return Task.FromResult(CommandResult.Success);
    }
}
