namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class EditCommand : ICommand
{
    public string Name => "edit";

    public string Description => "Edit a text file full-screen";

    public string Usage => "edit <file>";

    public IReadOnlyList<string> Aliases => ["nano"];

    public int MinArguments => 1;

    public int MaxArguments => 1;

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        if (context.Console.Screen is not { } screen)
        {
            return context.Fail("edit needs a text screen, which this shell does not have.");
        }

        if (FileCommandHelpers.RequireDisk(context) is { } noDisk)
        {
            return noDisk;
        }

        string path = context.ResolvePath(context.Args[0]);

        if (Directory.Exists(path))
        {
            return context.Fail($"{path} is a directory.");
        }

        if (Path.GetDirectoryName(path) is { } directory && !Directory.Exists(directory))
        {
            return context.Fail($"No such directory: {directory}");
        }

        try
        {
            bool finished = await new TextEditor(screen, path, context.Cancellation).RunAsync();
            return finished ? CommandResult.Success : CommandResult.Cancelled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return context.Fail($"Cannot edit {path}: {ex.Message}");
        }
    }
}
