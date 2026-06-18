namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class ShutdownCommand : ICommand
{
    public string Name => "shutdown";

    public string Description => "Shutdown the system";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        printLine("Shutting down system...");
        Sys.Power.Shutdown();
        return Task.CompletedTask;
    }
}