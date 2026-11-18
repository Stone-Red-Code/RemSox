namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// Runs one command at a time for a prompt, reporting unknown commands and supporting Ctrl+C.
/// </summary>
public sealed class CommandRunner
{
    private CancellationTokenSource? cancellation;

    public bool IsRunning => cancellation is not null;

    /// <summary>
    /// Runs <paramref name="line"/> through <see cref="CommandManager"/>, printing its output with
    /// <paramref name="printLine"/>, or a message when it is not a command. Blank lines do nothing.
    /// </summary>
    public async Task RunAsync(string line, Action<string> printLine)
    {
        if (string.IsNullOrWhiteSpace(line) || IsRunning)
        {
            return;
        }

        cancellation = new CancellationTokenSource();

        try
        {
            if (!await CommandManager.TryExecuteAsync(line, printLine, cancellation.Token))
            {
                printLine($"\"{line.Trim()}\" is not a command");
            }
        }
        catch (Exception ex)
        {
            printLine("Command failed: " + ex.Message);
        }
        finally
        {
            cancellation.Dispose();
            cancellation = null;
        }
    }

    /// <summary>Asks the running command, if any, to stop (Ctrl+C).</summary>
    public async Task CancelAsync()
    {
        if (cancellation is not null)
        {
            await cancellation.CancelAsync();
        }
    }
}
