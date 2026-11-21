using RemSox.Kernel.Accounts;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Security;

namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// RemSox shell scripts (<c>.rsh</c>): one command per line, as typed at a prompt, with blank
/// lines and lines starting with <c>#</c> ignored. Each account's <c>startup.rsh</c> runs when it
/// logs in (<see cref="Programs.UserStartup"/>).
/// </summary>
public static class ShellScript
{
    public const string FileExtension = ".rsh";

    /// <summary>How deeply scripts may run scripts, so one that runs itself stops instead of recursing forever.</summary>
    public const int MaxDepth = 8;

    /// <summary>Returns the commands of a script: its lines without blanks and comments, trimmed.</summary>
    public static List<string> ReadCommands(string path)
    {
        return [.. File.ReadAllLines(path).Select(line => line.Trim()).Where(IsCommand)];
    }

    /// <summary>Returns the commands of a script <paramref name="caller"/> may read.</summary>
    public static List<string> ReadCommands(Caller caller, string path)
    {
        return [.. UserFiles.ReadAllLines(caller, path).Select(line => line.Trim()).Where(IsCommand)];
    }

    /// <summary>
    /// Runs a script's commands in <paramref name="session"/>, one after another. A failing command
    /// is reported and the script goes on; Ctrl+C stops it.
    /// </summary>
    /// <returns>The exit code of the last command run, or of the reason the script could not run.</returns>
    public static async Task<int> RunAsync(string path, ShellSession session, ICommandConsole console, CancellationToken cancellation, bool elevated = false)
    {
        if (session.ScriptDepth >= MaxDepth)
        {
            console.WriteError($"{path}: scripts nested more than {MaxDepth} deep, not run.");
            return CommandResult.Failure;
        }

        List<string> commands;
        try
        {
            commands = ReadCommands(new Caller(session.Logon, elevated), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            console.WriteError($"Cannot read {path}: {ex.Message}");
            return CommandResult.Failure;
        }

        int exitCode = CommandResult.Success;
        session.ScriptDepth++;

        try
        {
            foreach (string command in commands)
            {
                if (cancellation.IsCancellationRequested)
                {
                    return CommandResult.Cancelled;
                }

                exitCode = await CommandManager.ExecuteAsync(command, session, console, cancellation, addToHistory: false, elevated);

                if (exitCode is not CommandResult.Success and not CommandResult.Cancelled)
                {
                    console.WriteError($"{Path.GetFileName(path)}: '{command}' failed (exit code {exitCode})");
                }
            }
        }
        finally
        {
            session.ScriptDepth--;
        }

        return exitCode;
    }

    private static bool IsCommand(string trimmedLine)
    {
        return trimmedLine.Length > 0 && trimmedLine[0] != '#';
    }
}
