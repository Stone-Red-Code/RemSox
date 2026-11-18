using RemSox.Kernel.Logging;
using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Processes;

/// <summary>
/// Runs the startup script (<see cref="ShellScript.InitScriptPath"/>) at boot, then ends.
/// </summary>
/// <remarks>
/// The script runs in a shell session of its own, through the same command system as the CLI.
/// The main loop keeps ticking meanwhile, so a command that waits on another process works.
/// Its output goes to this process's log (<c>logs &lt;id&gt;</c>), and errors are also shown on
/// the console. A command that asks a question gets no answer, which counts as "no".
/// </remarks>
internal sealed class AutostartProcess() : Process("Autostart"), ICommandConsole
{
    private readonly ShellSession session = new("init", canExit: false);

    internal override void Start(string[] args)
    {
        if (!File.Exists(ShellScript.InitScriptPath))
        {
            RequestStop();
            return;
        }

        _ = RunAsync();
    }

    internal override void Tick()
    {
    }

    private async Task RunAsync()
    {
        Logger.Log("Running " + ShellScript.InitScriptPath, LogSeverity.Info);
        int exitCode = await ShellScript.RunAsync(ShellScript.InitScriptPath, session, this, CancellationToken.None);
        Logger.Log($"{ShellScript.InitScriptPath} finished (exit code {exitCode}).", LogSeverity.Info);
        RequestStop();
    }

    // --- ICommandConsole ---

    public void WriteLine(string text)
    {
        Logger.Log(text, LogSeverity.Info);
    }

    public void WriteError(string text)
    {
        Logger.Log(text, LogSeverity.Error);

        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }

    public void Clear()
    {
    }

    public Task<string?> ReadLineAsync(string prompt, CancellationToken cancellation)
    {
        Logger.Log(prompt + "(no answer at boot)", LogSeverity.Warning);
        return Task.FromResult<string?>(null);
    }
}
