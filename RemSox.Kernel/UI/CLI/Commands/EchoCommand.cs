namespace RemSox.Kernel.UI.CLI.Commands;

/// <summary>
/// Prints its arguments, as <c>cmd</c>'s and Unix shells' <c>echo</c>: for scripts that tell the
/// user something, and with <c>write -c</c> (<c>write -c file echo text</c>) to put a line in a file.
/// </summary>
/// <remarks>
/// The words come out as the shell split them (quotes removed, escapes applied), joined by one
/// space, so <c>echo "a  b"</c> keeps both spaces and <c>echo</c> alone prints an empty line.
/// </remarks>
public sealed class EchoCommand : ICommand
{
    public string Name => "echo";

    public string Description => "Print text";

    public string Usage => "echo [text...]";

    public Task<int> ExecuteAsync(CommandContext context)
    {
        context.Console.WriteLine(string.Join(' ', context.Args));
        return Task.FromResult(CommandResult.Success);
    }
}
