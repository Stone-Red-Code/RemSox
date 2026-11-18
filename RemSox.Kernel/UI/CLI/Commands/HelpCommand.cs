namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class HelpCommand : ICommand
{
    public string Name => "help";

    public string Description => "List the commands, or show how to use one";

    public string Usage => "help [command]";

    public int MaxArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        if (context.Args.Count == 1)
        {
            ICommand? command = CommandManager.Find(context.Args[0]);
            if (command is null)
            {
                return Task.FromResult(context.Fail($"\"{context.Args[0]}\" is not a command."));
            }

            context.Output.WriteLine(command.Description);
            context.Output.WriteLine("Usage: " + command.Usage);

            if (command.Aliases.Count > 0)
            {
                context.Output.WriteLine("Also: " + string.Join(", ", command.Aliases));
            }

            return Task.FromResult(CommandResult.Success);
        }

        context.Output.WriteLine("Available commands ('help <command>' for details):");

        foreach (ICommand command in CommandManager.GetCommands())
        {
            context.Output.WriteLine($"  {command.Name,-12} - {command.Description}");
        }

        return Task.FromResult(CommandResult.Success);
    }
}
