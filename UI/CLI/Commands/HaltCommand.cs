namespace RemSox.UI.CLI.Commands;

public sealed class ShutdownCommand : ICommand
{
    public string Name => "shutdown";

    public string Description => "Shutdown the system";

    public void Execute(string? arguments, Action<string> printLine)
    {
        printLine("Shutting down system...");
        Sys.Power.Shutdown();
    }
}