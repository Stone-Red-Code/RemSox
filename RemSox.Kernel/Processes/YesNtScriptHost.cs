using System.Text;

using RemSox.Kernel.Security;

using YesNt.Interpreter.Runtime;

namespace RemSox.Kernel.Processes;

/// <summary>
/// A YesNt script's console and files in RemSox: its output goes to the terminal running it (or
/// its log), it reads the lines typed there, and it reads its script files, the one it was
/// started with and every one it imports, through <see cref="UserFiles"/> as whoever runs it.
/// </summary>
/// <remarks>
/// The interpreter asks it on the main loop, as it steps the script. Nothing here blocks: a line
/// is asked of the shell and the script waits (<see cref="InputStatus.Pending"/>) until the user
/// has entered it, so the rest of the system goes on meanwhile.
/// </remarks>
internal sealed class YesNtScriptHost(YesNtInterpreterProcess process) : IYesNtHost
{
    private readonly StringBuilder pendingOutput = new();
    private readonly CancellationTokenSource readCancellation = new();

    // The line being read for the script, until the user has entered it.
    private Task<string?>? pendingRead;

    /// <summary>Writes output, a whole line at a time; text without a line end waits for more.</summary>
    public void Write(string text)
    {
        _ = pendingOutput.Append(text);

        string buffered = pendingOutput.ToString();
        int lineEnd = buffered.LastIndexOf('\n');
        if (lineEnd < 0)
        {
            return;
        }

        foreach (string line in buffered[..lineEnd].Split('\n'))
        {
            process.PrintLine(line.TrimEnd('\r'));
        }

        _ = pendingOutput.Clear().Append(buffered[(lineEnd + 1)..]);
    }

    /// <summary>
    /// Asks the shell for a line, with output written without a line end (a question) as the
    /// prompt, so it stands where the user types; the script waits until it is entered.
    /// </summary>
    public InputStatus TryReadLine(out string line)
    {
        line = string.Empty;

        if (pendingRead is null)
        {
            if (process.ReadLine is not { } readLine)
            {
                process.PrintLine("This script reads input, but runs without a terminal.");
                return InputStatus.Closed;
            }

            pendingRead = readLine(TakePendingOutput(), readCancellation.Token);
        }

        if (!pendingRead.IsCompleted)
        {
            return InputStatus.Pending;
        }

        string? read = pendingRead.IsCompletedSuccessfully ? pendingRead.Result : null;
        pendingRead = null;
        if (read is null)
        {
            process.PrintLine("No more input: the script ends.");
            return InputStatus.Closed;
        }

        line = read;
        return InputStatus.Ready;
    }

    /// <summary>
    /// The first character of a line, since a shell hands programs whole lines: a key at a time
    /// would need the shell to pass keys.
    /// </summary>
    public InputStatus TryReadKey(out char key)
    {
        InputStatus status = TryReadLine(out string line);
        key = line.Length > 0 ? line[0] : ' ';
        return status;
    }

    /// <summary>Clears the terminal running the script, if it runs in one.</summary>
    public void Clear()
    {
        _ = pendingOutput.Clear();
        process.Clear?.Invoke();
    }

    /// <summary>Reads a script file if whoever runs the script may.</summary>
    /// <exception cref="UnauthorizedAccessException">They may not read it.</exception>
    public string[] ReadScript(string path)
    {
        return UserFiles.ReadAllLines(process.Caller, path);
    }

    /// <summary>Writes what is left of the output, and stops reading a line.</summary>
    public void Close()
    {
        readCancellation.Cancel();

        if (pendingOutput.Length > 0)
        {
            process.PrintLine(TakePendingOutput());
        }
    }

    private string TakePendingOutput()
    {
        string text = pendingOutput.ToString();
        _ = pendingOutput.Clear();
        return text;
    }
}
