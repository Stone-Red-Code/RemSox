namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class ExitCommand : ICommand
{
    public string Name => "exit";

    public string Description => "Close this shell (a Terminal window or Telnet session)";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        if (!context.Session.CanExit)
        {
            return Task.FromResult(context.Fail("The local console cannot be closed; use 'shutdown' to power off."));
        }

        context.Session.RequestExit();
        return Task.FromResult(CommandResult.Success);
    }
}
