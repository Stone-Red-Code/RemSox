namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class RebootCommand : ICommand
{
    public string Name => "reboot";

    public string Description => "Reboot the system";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        context.Console.WriteLine("Rebooting system...");
        Sys.Power.Reboot();
        return Task.FromResult(CommandResult.Success);
    }
}
