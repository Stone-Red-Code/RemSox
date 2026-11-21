using System.Collections.Concurrent;

using Cosmos.Kernel.System.Sessions;
using Cosmos.Network.Telnet;

using RemSox.Kernel.Accounts;
using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Processes;

/// <summary>
/// Serves the RemSox command line over Telnet. Every client gets a console session of its own
/// (listed next to the local one and reachable with Alt+Fn), logs in, and then gets a shell
/// with its own working directory and history, running the same commands as the CLI.
/// </summary>
/// <remarks>
/// A service: it runs as the system, and each client's shell as the account it logged in to.
/// Telnet is unencrypted, so names, passwords and everything typed can be read on the way.
/// </remarks>
internal sealed class TelnetProcess() : Process("Telnet Server")
{
    private TelnetServer? server;

    /// <summary>Gets the port the server listens on.</summary>
    public int Port { get; private set; }

    /// <param name="args">The port, already checked (or defaulted) by the <c>telnet</c> program's <c>Prepare</c>.</param>
    internal override void Start(string[] args)
    {
        int port = int.Parse(args[0]);
        Port = port;
        ListeningPorts.Claim(port, this);
        server = new TelnetServer(session => new TelnetShell(session).Run(), (ushort)port)
        {
            Log = message => Logger.Log(message, Logging.LogSeverity.Info),
        };
        server.Start();

        Logger.Log($"Telnet server started on port {port}.", Logging.LogSeverity.Info);
    }

    internal override void Tick()
    {
    }

    internal override void Stop()
    {
        server?.Stop();
        Logger.Log("Telnet server stopped.", Logging.LogSeverity.Info);
    }
}

/// <summary>
/// What a Telnet client gets: a login prompt, then a shell in the session it logged in to. Its
/// console session's thread only moves keys and text between the client and the main loop,
/// where the login and the shell run like every other one. Disconnecting ends the session.
/// </summary>
/// <remarks>
/// <see cref="Console"/> follows the thread, so only the session's own thread may read from or
/// write to the client. It passes every key to the <see cref="ShellFrontEnd"/> on the main loop,
/// and carries out what the shell queued for the terminal (as a <see cref="ConsoleTerminal"/>
/// on that thread). It cannot wait for a key, which would keep it from writing output, so it
/// polls.
/// </remarks>
internal sealed class TelnetShell : IShellTerminal, ILoginGate
{
    private readonly ConsoleSession consoleSession;
    private readonly ConsoleTerminal console = new();
    private readonly ConcurrentQueue<Action> terminalActions = new();
    private readonly TextLogin login;

    // Set on the main loop once the client logged in.
    private ShellFrontEnd? frontEnd;
    private LogonSession? logon;

    // Set on the main loop once the shell ran exit, the session ended or the login gave up; the
    // session thread then disconnects.
    private volatile bool exitRequested;

    // The client's window size, read on the session thread (Console follows the thread).
    private volatile int width = 80;
    private volatile int height = 24;

    public TelnetShell(ConsoleSession consoleSession)
    {
        this.consoleSession = consoleSession;
        login = new TextLogin(this, LogonKind.Telnet, consoleSession.Name, canCreateFirstAccount: false, maxFailures: 3, gate: this);
        login.LoggedIn += OnLoggedIn;
        login.GaveUp += () => exitRequested = true;
    }

    /// <summary>Runs the login and then the shell, until the client types <c>exit</c> or disconnects.</summary>
    public void Run()
    {
        Console.WriteLine("Welcome to RemSox! (" + consoleSession.Name + ")");
        MainLoop.Post(() =>
        {
            LogonSessions.Ended += OnSessionEnded;
            login.Start();
        });

        try
        {
            while (!consoleSession.IsClosed)
            {
                UpdateSize();
                ForwardKeys();

                if (login.IsWaitOver)
                {
                    MainLoop.Post(login.Tick);
                }

                RunTerminalActions();

                if (exitRequested)
                {
                    // What exit printed may have been queued after the actions just run.
                    RunTerminalActions();
                    return;
                }

                Thread.Sleep(10);
            }
        }
        catch (EndOfStreamException)
        {
            // The client disconnected while a key was being read.
        }
        finally
        {
            MainLoop.Post(Close);
        }
    }

    /// <summary>Runs on the main loop once the client logged in: starts its shell.</summary>
    private void OnLoggedIn(LogonSession session)
    {
        logon = session;
        ShellFrontEnd shell = new(new ShellSession(consoleSession.Name, canExit: true, session.Account.HomeFolder, session), this);
        shell.CommandFinished += _ =>
        {
            // Only ever set: logout already set it while the command ran.
            if (shell.Session.ExitRequested)
            {
                exitRequested = true;
            }
        };
        frontEnd = shell;
        shell.WriteLine($"Logged in as {session.Account.Name}. Type 'help' to see the commands, 'exit' to disconnect.");
        shell.Start();
    }

    private void OnSessionEnded(LogonSession session)
    {
        if (session == logon)
        {
            exitRequested = true;
        }
    }

    /// <summary>Runs on the main loop once the client is gone: ends its shell and its session.</summary>
    private void Close()
    {
        LogonSessions.Ended -= OnSessionEnded;
        frontEnd?.Close();

        if (logon is not null)
        {
            LogonSessions.End(logon);
        }
    }

    /// <summary>Runs on the main loop: a key goes to the shell once logged in, else to the login.</summary>
    private void HandleKey(ConsoleKeyInfo key)
    {
        if (frontEnd is not null)
        {
            frontEnd.HandleKey(key);
        }
        else
        {
            login.HandleKey(key);
        }
    }

    private void UpdateSize()
    {
        int newWidth = Console.WindowWidth;
        int newHeight = Console.WindowHeight;

        if (newWidth != width || newHeight != height)
        {
            width = newWidth;
            height = newHeight;
            MainLoop.Post(() => Resized?.Invoke());
        }
    }

    private void ForwardKeys()
    {
        while (Console.KeyAvailable && !consoleSession.IsClosed)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            MainLoop.Post(() => HandleKey(key));
        }
    }

    private void RunTerminalActions()
    {
        while (terminalActions.TryDequeue(out Action? action))
        {
            action();
        }
    }

    // --- ILoginGate: Telnet clients can reconnect or connect many times at once, so failures
    // count across all of them (LoginThrottle), not only at this prompt ---

    public string? Refuse(string name)
    {
        return LoginThrottle.Refuse(name);
    }

    public void Report(string name, bool rightPassword)
    {
        if (rightPassword)
        {
            LoginThrottle.Succeeded(name);
        }
        else
        {
            LoginThrottle.Failed(name);
        }
    }

    // --- IShellTerminal: called on the main loop, carried out on the session thread ---

    public void WriteLine(string text)
    {
        terminalActions.Enqueue(() => console.WriteLine(text));
    }

    public void WriteError(string text)
    {
        terminalActions.Enqueue(() => console.WriteError(text));
    }

    public void Clear()
    {
        terminalActions.Enqueue(console.Clear);
    }

    public void ShowInput(string? line)
    {
        terminalActions.Enqueue(() => console.ShowInput(line));
    }

    public void CommitInput()
    {
        terminalActions.Enqueue(console.CommitInput);
    }

    /// <summary>Raised on the main loop when the client reports a new window size.</summary>
    public event Action? Resized;

    public int Width => width;

    public int Height => height;

    public void BeginScreen()
    {
        terminalActions.Enqueue(console.BeginScreen);
    }

    public void EndScreen()
    {
        terminalActions.Enqueue(console.EndScreen);
    }

    public void WriteAt(int column, int row, string text, bool inverse)
    {
        terminalActions.Enqueue(() => console.WriteAt(column, row, text, inverse));
    }

    public void SetCursor(int column, int row)
    {
        terminalActions.Enqueue(() => console.SetCursor(column, row));
    }
}
