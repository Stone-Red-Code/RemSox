using RemSox.Kernel.Logging;
using RemSox.Kernel.Processing;

namespace RemSox.Kernel.Accounts;

/// <summary>How a logon session was started.</summary>
public enum LogonKind
{
    /// <summary>The kernel's own session; never logged in to and never ended.</summary>
    System,

    /// <summary>At the machine: the console prompt or the login window.</summary>
    Console,

    /// <summary>From a Telnet client.</summary>
    Telnet,

    /// <summary>From a remote desktop viewer.</summary>
    RemoteDesktop,
}

/// <summary>
/// One login: who logged in, how and from where. Every process belongs to one (see
/// <see cref="Process.Session"/>), passed on to whatever it starts, so ending the session stops
/// everything its user started.
/// </summary>
public sealed class LogonSession
{
    internal LogonSession(int id, Account account, LogonKind kind, string origin)
    {
        Id = id;
        Account = account;
        Kind = kind;
        Origin = origin;
    }

    /// <summary>Gets the session's number: 0 for the system's, counting up from 1 per boot.</summary>
    public int Id { get; }

    public Account Account { get; }

    public LogonKind Kind { get; }

    /// <summary>Gets where the session came from: <c>tty1</c>, <c>screen</c> or a client's address.</summary>
    public string Origin { get; }

    public DateTime StartedAt { get; } = DateTime.Now;

    /// <summary>Gets whether this is the system's session, which services and the boot run in.</summary>
    public bool IsSystem => Kind == LogonKind.System;

    /// <summary>Gets whether the session has ended; nothing may start in it any more.</summary>
    public bool HasEnded { get; internal set; }

    public override string ToString()
    {
        return $"{Account.Name} ({Kind}, {Origin})";
    }
}

/// <summary>The logon sessions that are running, and the system's own one.</summary>
public static class LogonSessions
{
    private static readonly List<LogonSession> active = [];
    private static int nextId = 1;

    /// <summary>
    /// Gets the system's session: the kernel's processes, the startup script and services run
    /// in it. It never ends.
    /// </summary>
    public static LogonSession System { get; } = new(0, Account.System, LogonKind.System, "kernel");

    /// <summary>Gets the sessions users are logged in to, oldest first.</summary>
    public static IReadOnlyList<LogonSession> Active => active;

    /// <summary>
    /// Raised on the main loop when someone has logged in, with the new session; the user's
    /// startup script runs from here (<see cref="Programs.UserStartup"/>).
    /// </summary>
    public static event Action<LogonSession>? Started;

    /// <summary>
    /// Raised on the main loop when a session ends, after its processes were asked to stop.
    /// Shells running in it (the console, Telnet) log out or disconnect.
    /// </summary>
    public static event Action<LogonSession>? Ended;

    /// <summary>Starts a session for <paramref name="account"/>, creating its profile's folders if they are missing.</summary>
    public static LogonSession Start(Account account, LogonKind kind, string origin)
    {
        MainLoop.AssertMainThread(nameof(Start));

        if (account.IsSystem || kind == LogonKind.System)
        {
            throw new ArgumentException("The system account cannot log in.", nameof(account));
        }

        AccountStore.EnsureProfile(account);

        LogonSession session = new(nextId++, account, kind, origin);
        active.Add(session);
        SystemLog.Log($"{account.Name} logged in (session {session.Id}, {kind}, {origin}).", LogSeverity.Info);
        Started?.Invoke(session);
        return session;
    }

    /// <summary>
    /// Ends <paramref name="session"/>: stops every process in it and tells its shells. Ending a
    /// session twice, or the system's, does nothing.
    /// </summary>
    public static void End(LogonSession session)
    {
        MainLoop.AssertMainThread(nameof(End));

        if (session.IsSystem || session.HasEnded)
        {
            return;
        }

        session.HasEnded = true;
        _ = active.Remove(session);

        foreach (Process process in ProcessManager.GetAllProcesses().Where(p => p.Session == session).ToList())
        {
            ProcessManager.StopProcess(process.Id);
        }

        SystemLog.Log($"{session.Account.Name} logged out (session {session.Id}).", LogSeverity.Info);
        Ended?.Invoke(session);
    }

    /// <summary>
    /// Ends the console sessions that have nothing left in them: once the desktop or the console
    /// shell holding one is gone (stopped, or crashed), the session would otherwise stay logged
    /// in forever. Called before the console's login screen comes back.
    /// </summary>
    public static void EndIdleConsoleSessions()
    {
        foreach (LogonSession session in active.Where(s => s.Kind == LogonKind.Console).ToList())
        {
            if (!ProcessManager.GetAllProcesses().Any(p => p.Session == session))
            {
                End(session);
            }
        }
    }
}
