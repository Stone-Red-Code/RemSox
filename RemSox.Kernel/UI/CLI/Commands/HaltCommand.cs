namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class ShutdownCommand : ICommand
{
    public string Name => "shutdown";

    public string Description => "Shut down the system";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        context.Output.WriteLine("Shutting down system...");
        Sys.Power.Shutdown();
        return Task.FromResult(CommandResult.Success);
    }
}
