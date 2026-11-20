namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// Runs a shell's commands one at a time, each with its own cancellation for Ctrl+C.
/// </summary>
public sealed class CommandRunner(ShellSession session, ICommandConsole console)
{
    private CancellationTokenSource? cancellation;

    public ShellSession Session { get; } = session;

    public bool IsRunning => cancellation is not null;

    /// <summary>Gets the exit code of the last command run.</summary>
    public int LastExitCode { get; private set; }

    /// <summary>
    /// Runs <paramref name="line"/>. Blank lines, and lines entered while a command runs, do nothing.
    /// </summary>
    public async Task<int> RunAsync(string line)
    {
        Processing.MainLoop.AssertMainThread("CommandRunner.RunAsync");

        if (string.IsNullOrWhiteSpace(line) || IsRunning)
        {
            return LastExitCode;
        }

        CancellationTokenSource run = new();
        cancellation = run;

        try
        {
            LastExitCode = await CommandManager.ExecuteAsync(line, Session, console, run.Token);
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            LastExitCode = CommandResult.Cancelled;
        }
        catch (Exception ex)
        {
            console.WriteError("Command failed: " + ex.Message);
            LastExitCode = CommandResult.Failure;
        }
        finally
        {
            cancellation = null;
            run.Dispose();
        }

        if (LastExitCode == CommandResult.Cancelled)
        {
            console.WriteError("^C");
        }

        return LastExitCode;
    }

    /// <summary>Cancels the running command, if any (Ctrl+C).</summary>
    public void Cancel()
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The command finished in the meantime.
        }
    }
}
