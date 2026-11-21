using RemSox.Kernel.Accounts;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;

namespace RemSox.Kernel.Programs;

/// <summary>
/// Each account's own startup script, <c>/users/&lt;name&gt;/config/startup.rsh</c>: run in the new session
/// every time its user logs in at the machine (console or login window) or by remote desktop, like
/// Windows' Startup folder. That is where "start the desktop when I log in" goes; what runs for
/// the machine whoever logs in is a service (<see cref="ServiceList"/>).
/// </summary>
/// <remarks>
/// Telnet logins do not run it: a line such as <c>desktop</c> would take the machine's screen
/// for a session at the other end of a network. It runs without a window (<c>noterminal</c>),
/// so what it prints goes to its process's log (<c>logs</c>).
/// </remarks>
public static class UserStartup
{
    /// <summary>The script's name in the home folder.</summary>
    public const string FileName = "startup.rsh";

    private static readonly string[] DefaultScript =
    [
        "# Your startup script: run each time you log in at the machine or by remote",
        "# desktop (not over Telnet), one command per line, as if typed at a prompt.",
        "# '#' starts a comment. Its output goes to the log ('logs').",
        "#",
        "# Examples:",
        "# desktop        (start the desktop after logging in at the console prompt)",
        "# explorer",
    ];

    /// <summary>Returns the path of <paramref name="account"/>'s startup script.</summary>
    public static string GetPath(Account account)
    {
        return account.ConfigFolder + "/" + FileName;
    }

    /// <summary>Runs the startup script of a session that just started, if its kind runs one and the file exists.</summary>
    public static void Run(LogonSession session)
    {
        if (!session.RunsStartup || !FileSystemManager.IsMounted)
        {
            return;
        }

        string path = GetPath(session.Account);
        if (!File.Exists(path))
        {
            return;
        }

        ProgramStart start = ProgramRegistry.Find(BuiltInPrograms.NoTerminalName) is { } noTerminal
            ? ProgramRegistry.Start(noTerminal, [path], session.Account.HomeFolder, new Caller(session, Elevated: false))
            : ProgramStart.Fail("There is no noterminal program.");

        if (start.Error is not null)
        {
            SystemLog.Log($"Could not run {path}: {start.Error}", LogSeverity.Error);
        }
    }

    /// <summary>Writes a new account's startup script, with commented examples; failures are logged.</summary>
    public static void WriteDefault(Account account)
    {
        string path = GetPath(account);

        try
        {
            if (!File.Exists(path))
            {
                SafeFile.WriteAllLines(path, DefaultScript);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SystemLog.Log($"Could not create {path}: {ex.Message}", LogSeverity.Error);
        }
    }
}
