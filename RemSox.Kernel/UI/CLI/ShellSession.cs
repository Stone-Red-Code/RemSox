namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// The state of one shell: its working directory and command history. Every shell (the local CLI,
/// each GUI Terminal, each Telnet client) has its own, so one shell's <c>cd</c> or history does
/// not change another's.
/// </summary>
/// <param name="name">The shell's name, such as "tty1" or a Telnet client's address.</param>
/// <param name="canExit">Whether <c>exit</c> may close the shell.</param>
/// <param name="workingDirectory">Where the shell starts: its process's working directory.</param>
public sealed class ShellSession(string name, bool canExit, string workingDirectory)
{
    private readonly List<string> history = [];

    /// <summary>Gets the shell's name, such as "tty1" or a Telnet client's address.</summary>
    public string Name { get; } = name;

    /// <summary>Gets whether <c>exit</c> may close this shell (the local console cannot be closed).</summary>
    public bool CanExit { get; } = canExit;

    /// <summary>Gets or sets how many scripts are running inside each other in this shell.</summary>
    public int ScriptDepth { get; set; }

    /// <summary>Gets whether <c>exit</c> asked the shell to close.</summary>
    public bool ExitRequested { get; private set; }

    /// <summary>Gets or sets the directory relative paths are resolved against.</summary>
    public string WorkingDirectory { get; set; } = workingDirectory;

    public int HistoryCount
    {
        get
        {
            return history.Count;
        }
    }

    /// <summary>Returns the history entry at <paramref name="index"/>, oldest first.</summary>
    public string GetHistory(int index)
    {
        return history[index];
    }

    /// <summary>Records a command, unless it repeats the previous one.</summary>
    public void AddHistory(string line)
    {
        if (history.Count == 0 || history[^1] != line)
        {
            history.Add(line);
        }
    }

    /// <summary>Resolves <paramref name="path"/> against <see cref="WorkingDirectory"/>.</summary>
    public string ResolvePath(string path)
    {
        return Path.GetFullPath(path, WorkingDirectory);
    }

    public void RequestExit()
    {
        ExitRequested = CanExit;
    }
}
