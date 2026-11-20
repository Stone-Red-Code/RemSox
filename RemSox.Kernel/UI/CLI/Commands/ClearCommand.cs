namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class ClearCommand : ICommand
{
    public string Name => "clear";

    public string Description => "Clear the screen";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        context.Console.Clear();
        return Task.FromResult(CommandResult.Success);
    }
}
