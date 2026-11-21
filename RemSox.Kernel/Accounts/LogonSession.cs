using RemSox.Kernel.Logging;
using RemSox.Kernel.Processing;

namespace RemSox.Kernel.Accounts;

/// <summary>
/// How a logon session was started. It never changes, so it says nothing about where the session
/// is now: a desktop session started at the machine stays <see cref="Local"/> while remote
/// desktop shows it. That is <see cref="LogonSession.Place"/>.
/// </summary>
public enum LogonKind
{
    /// <summary>The kernel's own session; never logged in to and never ended.</summary>
    System,

    /// <summary>At the machine itself: the login prompt or the login window.</summary>
    Local,

    /// <summary>From a Telnet client.</summary>
    Telnet,

    /// <summary>From a remote desktop viewer.</summary>
    RemoteDesktop,
}

/// <summary>Where a logon session is now, as opposed to how it was started (<see cref="LogonKind"/>).</summary>
public enum SessionPlace
{
    /// <summary>The kernel's own session.</summary>
    System,

    /// <summary>At the machine: its display and keyboard are this session's.</summary>
    Machine,

    /// <summary>Shown by remote desktop viewers.</summary>
    RemoteDesktop,

    /// <summary>A Telnet login, which never moves.</summary>
    Telnet,

    /// <summary>Running but shown nowhere: locked, or its viewer closed. Its user resumes it by logging in.</summary>
    Locked,
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

    /// <summary>Gets how the session was logged in to; it never changes (where it is now: <see cref="Place"/>).</summary>
    public LogonKind Kind { get; }

    /// <summary>
    /// Gets where the session was logged in from: <c>login prompt</c> or <c>login window</c> at
    /// the machine, or a client's address. Like <see cref="Kind"/>, it never changes.
    /// </summary>
    public string Origin { get; }

    public DateTime StartedAt { get; } = DateTime.Now;

    /// <summary>Gets whether this is the system's session, which services and the boot run in.</summary>
    public bool IsSystem => Kind == LogonKind.System;

    /// <summary>
    /// Gets whether the user sits at the machine itself (<see cref="LogonSessions.AtMachine"/>):
    /// they may turn it off, their keyboard layout applies, and the machine's display shows their
    /// desktop. A desktop session moves between the machine and remote desktop, so this follows
    /// the session rather than how it was started.
    /// </summary>
    public bool IsAtMachine => LogonSessions.AtMachine == this;

    /// <summary>
    /// Gets whether the login is a full one with a screen of its own (at the machine, or by remote
    /// desktop), which runs the account's startup script, like Windows' interactive logons.
    /// </summary>
    public bool RunsStartup => Kind is LogonKind.Local or LogonKind.RemoteDesktop;

    /// <summary>
    /// Gets where the session is now, for whatever tells the user or decides by it (turning the
    /// machine off, the session list); worked out from the live state each time.
    /// </summary>
    public SessionPlace Place => IsSystem ? SessionPlace.System
        : IsAtMachine ? SessionPlace.Machine
        : Kind == LogonKind.Telnet ? SessionPlace.Telnet
        : Viewers > 0 ? SessionPlace.RemoteDesktop
        : SessionPlace.Locked;

    /// <summary>Gets or sets how many remote desktop viewers show the session; kept by the remote desktop server.</summary>
    public int Viewers { get; internal set; }

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
    /// Gets the session of whoever is logged in at the machine itself, or null while nobody is
    /// (the login prompt or window shows). A console login takes the machine; a remote desktop
    /// viewer joining that session moves it away again (<see cref="MoveToMachine"/>).
    /// </summary>
    public static LogonSession? AtMachine { get; private set; }

    /// <summary>Raised on the main loop when <see cref="AtMachine"/> changed; the machine's keyboard layout follows it.</summary>
    public static event Action? MachineChanged;

    /// <summary>
    /// Raised on the main loop when a session was locked (<see cref="Lock"/>); remote desktop
    /// disconnects its viewers.
    /// </summary>
    public static event Action<LogonSession>? Locked;

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

        if (kind == LogonKind.Local)
        {
            MoveToMachine(session);
        }

        Started?.Invoke(session);
        return session;
    }

    /// <summary>
    /// Makes <paramref name="session"/> the one at the machine, or nobody's for null: logging in
    /// at the machine to an account's running desktop session brings it here, and a remote
    /// desktop viewer joining it takes it away.
    /// </summary>
    public static void MoveToMachine(LogonSession? session)
    {
        MainLoop.AssertMainThread(nameof(MoveToMachine));

        if (session is { IsSystem: true } or { HasEnded: true } || AtMachine == session)
        {
            return;
        }

        AtMachine = session;
        MachineChanged?.Invoke();
    }

    /// <summary>
    /// Locks <paramref name="session"/>: it keeps running, but no display shows it any more. At
    /// the machine the login screen comes back, where its user logs in to resume it or someone
    /// else logs in beside it; its remote desktop viewers are disconnected. Only a desktop
    /// session can be locked meaningfully, which the caller checks.
    /// </summary>
    public static void Lock(LogonSession session)
    {
        MainLoop.AssertMainThread(nameof(Lock));

        if (session.IsSystem || session.HasEnded)
        {
            return;
        }

        if (AtMachine == session)
        {
            MoveToMachine(null);
        }

        SystemLog.Log($"{session.Account.Name} locked session {session.Id}.", LogSeverity.Info);
        Locked?.Invoke(session);
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

        if (AtMachine == session)
        {
            AtMachine = null;
            MachineChanged?.Invoke();
        }

        foreach (Process process in ProcessManager.GetAllProcesses().Where(p => p.Session == session).ToList())
        {
            ProcessManager.StopProcess(process.Id);
        }

        SystemLog.Log($"{session.Account.Name} logged out (session {session.Id}).", LogSeverity.Info);
        Ended?.Invoke(session);
    }

    /// <summary>
    /// Ends the session at the machine if it has nothing left in it: once the desktop or the
    /// console shell holding it is gone (stopped, or crashed), it would otherwise stay logged in
    /// forever. Called before the machine's login screen comes back.
    /// </summary>
    public static void EndIdleSessionAtMachine()
    {
        if (AtMachine is { } session && !ProcessManager.GetAllProcesses().Any(p => p.Session == session))
        {
            End(session);
        }
    }
}
