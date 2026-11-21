using RemSox.Kernel.Accounts;
using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Processes;

/// <summary>
/// The local console: a login prompt (<see cref="TextLogin"/>), then a shell running as whoever
/// logged in. <c>exit</c> or <c>logout</c> ends that session and the prompt comes back.
/// </summary>
/// <remarks>
/// <para>
/// The process itself is the system's: the console exists before anyone logs in. Only the shell
/// runs in the user's session. The kernel stops this process while a desktop or the login window
/// has the screen; that does not end the session, which the desktop goes on using.
/// </para>
/// <para>
/// Keys are read without blocking from <see cref="Tick"/>, on the kernel's main loop: a blocking
/// Console.ReadLine would keep reading the keyboard after the desktop starts and steal its keys.
/// A command runs without holding up the loop, so later ticks can deliver Ctrl+C and the answers
/// to its questions.
/// </para>
/// </remarks>
internal sealed class CliProcess() : Process("Console")
{
    private const string ConsoleName = "tty1";

    private readonly ConsoleTerminal terminal = new();

    /// <summary>
    /// Gets what went wrong at boot (services that did not start), shown once under the first
    /// login prompt's welcome so it does not go unnoticed; it is in the log too.
    /// </summary>
    internal static List<string> BootErrors { get; } = [];
    private TextLogin? login;
    private ShellFrontEnd? frontEnd;

    internal override void Start(string[] args)
    {
        LogonSessions.Ended += OnSessionEnded;
        ShowLogin();
    }

    internal override void Tick()
    {
        login?.Tick();

        while (Console.KeyAvailable)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (frontEnd is not null)
            {
                frontEnd.HandleKey(key);
            }
            else
            {
                login?.HandleKey(key);
            }
        }
    }

    internal override void Stop()
    {
        LogonSessions.Ended -= OnSessionEnded;
        frontEnd?.Close();
    }

    private void ShowLogin()
    {
        frontEnd = null;
        Console.Clear();
        terminal.WriteLine("Welcome to RemSox! (" + ConsoleName + ")");

        foreach (string error in BootErrors)
        {
            terminal.WriteError(error);
        }

        BootErrors.Clear();

        login = new TextLogin(terminal, LogonKind.Console, ConsoleName, canCreateFirstAccount: true, maxFailures: 0);
        login.LoggedIn += StartShell;
        login.Start();
    }

    private void StartShell(LogonSession session)
    {
        login = null;
        ShellFrontEnd shell = new(new ShellSession(ConsoleName, canExit: true, session.Account.HomeFolder, session), terminal);
        shell.CommandFinished += _ =>
        {
            if (shell.Session.ExitRequested)
            {
                LogonSessions.End(session);
            }
        };

        frontEnd = shell;
        shell.WriteLine($"Logged in as {session.Account.Name}. Type 'help' to see the commands, 'logout' to log out.");
        shell.Start();
    }

    private void OnSessionEnded(LogonSession session)
    {
        if (frontEnd?.Session.Logon == session)
        {
            frontEnd.Close();
            ShowLogin();
        }
    }
}
