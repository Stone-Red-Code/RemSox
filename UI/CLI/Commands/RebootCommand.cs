namespace RemSox.UI.CLI.Commands;

public class RebootCommand : ICommand
{
    public string Name => "reboot";

    public string Description => "Reboot the system";

    public void Execute(string? arguments, Action<string> printLine)
    {
        printLine("Rebooting system...");
        Sys.Power.Reboot();
    }
}
