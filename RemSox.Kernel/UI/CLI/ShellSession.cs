namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// The state of one shell: its working directory and command history. Every shell (the local CLI,
/// each GUI Terminal, each Telnet client) has its own, so one shell's <c>cd</c> or history does
/// not change another's.
/// </summary>
public sealed class ShellSession(string name, bool canExit)
{
    private readonly List<string> history = [];
    private readonly Lock historyLock = new();

    /// <summary>Gets the shell's name, such as "tty1" or a Telnet client's address.</summary>
    public string Name { get; } = name;

    /// <summary>Gets whether <c>exit</c> may close this shell (the local console cannot be closed).</summary>
    public bool CanExit { get; } = canExit;

    /// <summary>Gets whether <c>exit</c> asked the shell to close.</summary>
    public bool ExitRequested { get; private set; }

    /// <summary>
    /// Gets or sets the directory relative paths are resolved against. Starts at the kernel's
    /// current directory (the mounted disk, once mounted).
    /// </summary>
    public string WorkingDirectory { get; set; } = GetInitialDirectory();

    public int HistoryCount
    {
        get
        {
            lock (historyLock)
            {
                return history.Count;
            }
        }
    }

    /// <summary>Returns the history entry at <paramref name="index"/>, oldest first.</summary>
    public string GetHistory(int index)
    {
        lock (historyLock)
        {
            return history[index];
        }
    }

    /// <summary>Records a command, unless it repeats the previous one.</summary>
    public void AddHistory(string line)
    {
        lock (historyLock)
        {
            if (history.Count == 0 || history[^1] != line)
            {
                history.Add(line);
            }
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

    private static string GetInitialDirectory()
    {
        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch (IOException)
        {
            return "/";
        }
    }
}
