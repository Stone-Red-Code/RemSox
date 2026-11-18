namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class RunScriptCommand : ICommand
{
    public string Name => "run";

    public string Description => "Run a shell script (.rsh) in this shell; typing its path runs it too";

    public string Usage => "run <script.rsh>";

    public int MinArguments => 1;

    public int MaxArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        string path = context.ResolvePath(context.Args[0]);

        if (!File.Exists(path))
        {
            return Task.FromResult(context.Fail("Script not found: " + path));
        }

        // In this shell, so a script's cd changes the directory of the shell that ran it.
        return ShellScript.RunAsync(path, context.Session, context.Console, context.Cancellation);
    }
}
