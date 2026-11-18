using RemSox.Kernel.FileSystem;

namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// RemSox shell scripts (<c>.rsh</c>): one command per line, as typed at a prompt, with blank
/// lines and lines starting with <c>#</c> ignored. <see cref="InitScriptPath"/> runs at boot.
/// </summary>
public static class ShellScript
{
    public const string FileExtension = ".rsh";

    public const string InitScriptPath = FileSystemManager.MountPoint + "/etc/init" + FileExtension;

    /// <summary>How deeply scripts may run scripts, so one that runs itself stops instead of recursing forever.</summary>
    public const int MaxDepth = 8;

    private static readonly string[] DefaultInitScript =
    [
        "# RemSox startup script: run at boot, one command per line, in order, as if typed at a",
        "# prompt. Edit it with the 'autostart' command, or any way you edit files. '#' starts a comment.",
        "#",
        "# Examples:",
        "# rdp-start 9999",
        "# telnet-start",
        "# start-gui",
    ];

    /// <summary>Returns the commands of a script: its lines without blanks and comments, trimmed.</summary>
    public static List<string> ReadCommands(string path)
    {
        return [.. File.ReadAllLines(path).Select(line => line.Trim()).Where(IsCommand)];
    }

    /// <summary>Creates the startup script with commented examples when a disk is mounted and it is missing.</summary>
    public static void EnsureInitScript()
    {
        if (FileSystemManager.IsMounted && !File.Exists(InitScriptPath))
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(InitScriptPath)!);
            File.WriteAllLines(InitScriptPath, DefaultInitScript);
        }
    }

    /// <summary>Appends a command to a script.</summary>
    public static void AddCommand(string path, string command)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        List<string> lines = File.Exists(path) ? [.. File.ReadAllLines(path)] : [];
        lines.Add(command);
        File.WriteAllLines(path, lines);
    }

    /// <summary>
    /// Removes the command at <paramref name="index"/> (0-based, as <see cref="ReadCommands"/> lists
    /// them), keeping comments and blank lines.
    /// </summary>
    /// <returns>The removed command, or null when there is none at that index.</returns>
    public static string? RemoveCommand(string path, int index)
    {
        List<string> lines = [.. File.ReadAllLines(path)];
        List<int> commandLines = [.. lines.Select((line, i) => (line, i)).Where(x => IsCommand(x.line.Trim())).Select(x => x.i)];

        if (index < 0 || index >= commandLines.Count)
        {
            return null;
        }

        string removed = lines[commandLines[index]].Trim();
        lines.RemoveAt(commandLines[index]);
        File.WriteAllLines(path, lines);
        return removed;
    }

    /// <summary>
    /// Runs a script's commands in <paramref name="session"/>, one after another. A failing command
    /// is reported and the script goes on; Ctrl+C stops it.
    /// </summary>
    /// <returns>The exit code of the last command run, or of the reason the script could not run.</returns>
    public static async Task<int> RunAsync(string path, ShellSession session, ICommandConsole console, CancellationToken cancellation)
    {
        if (session.ScriptDepth >= MaxDepth)
        {
            console.WriteError($"{path}: scripts nested more than {MaxDepth} deep, not run.");
            return CommandResult.Failure;
        }

        List<string> commands;
        try
        {
            commands = ReadCommands(path);
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

                exitCode = await CommandManager.ExecuteAsync(command, session, console, cancellation, addToHistory: false);

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
