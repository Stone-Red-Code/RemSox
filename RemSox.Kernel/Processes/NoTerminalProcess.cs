using RemSox.Kernel.Logging;
using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Processes;

/// <summary>
/// Runs a command line in a shell that has no Terminal window, then ends: <c>noterminal demo.ynt</c>
/// shows only the windows the script opens. Like <c>terminal &lt;line&gt;</c> minus the Terminal,
/// so a shortcut (<c>run = noterminal</c>), a file association or a shell can start anything
/// without a console, and the boot runs the startup script through it.
/// </summary>
/// <remarks>
/// The line runs in a shell session of its own, through the same command system as every shell.
/// Its output goes to this process's log (<c>logs &lt;id&gt;</c>); a command that asks a question
/// gets no answer, which counts as "no". Stopping the process (<c>kill</c>) cancels the line like
/// Ctrl+C.
/// </remarks>
public sealed class NoTerminalProcess() : Process("noterminal"), ICommandConsole
{
    private static int sessionCount;

    private CommandRunner runner = null!;
    private string line = string.Empty;
    private bool started;

    /// <summary>
    /// Gets or sets whether errors also go to the local console. The boot sets it for the startup
    /// script, whose errors would otherwise go unnoticed; a user's <c>noterminal</c> never shows
    /// anything of its own.
    /// </summary>
    internal bool EchoErrorsToConsole { get; set; }

    internal override void Start(string[] args)
    {
        runner = new CommandRunner(new ShellSession("noterminal" + ++sessionCount, canExit: true, WorkingDirectory, Session), this);
        line = string.Join(' ', args.Select(CommandLine.Quote));
        Name = "noterminal " + line;
    }

    internal override void Tick()
    {
        // Run from the first tick, not Start, so whoever spawned it can set it up first.
        if (!started)
        {
            started = true;
            _ = RunAsync();
        }
    }

    internal override void Stop()
    {
        runner.Cancel();
    }

    private async Task RunAsync()
    {
        Logger.Log("Running " + line, LogSeverity.Info);
        int exitCode = await runner.RunAsync(line);
        Logger.Log($"{line} finished (exit code {exitCode}).", exitCode == CommandResult.Success ? LogSeverity.Info : LogSeverity.Warning);
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

        if (EchoErrorsToConsole)
        {
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(text);
            Console.ForegroundColor = previous;
        }
    }

    public void Clear()
    {
    }

    public Task<string?> ReadLineAsync(string prompt, CancellationToken cancellation)
    {
        Logger.Log(prompt + "(no answer without a Terminal)", LogSeverity.Warning);
        return Task.FromResult<string?>(null);
    }
}
