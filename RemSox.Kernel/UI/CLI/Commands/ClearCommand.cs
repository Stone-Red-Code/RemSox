namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class ClearCommand : ICommand
{
    public string Name => "clear";

    public string Description => "Clear the screen";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        Console.Clear();
        return Task.CompletedTask;
    }
}