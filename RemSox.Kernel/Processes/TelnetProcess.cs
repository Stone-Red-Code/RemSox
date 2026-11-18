using System.Collections.Concurrent;

using Cosmos.Kernel.System.Sessions;
using Cosmos.Network.Telnet;

using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Processes;

/// <summary>
/// Serves the RemSox command line over Telnet. Every client gets a console session of its own
/// (listed next to the local one and reachable with Alt+Fn) and a shell with its own working
/// directory and history, running the same commands as the CLI.
/// </summary>
/// <remarks>
/// Telnet is unencrypted and has no login: anyone who reaches the port gets a shell.
/// </remarks>
internal sealed class TelnetProcess() : Process("Telnet Server")
{
    private TelnetServer? server;

    public int Port { get; private set; }

    internal override void Start(string[] args)
    {
        int port = TelnetServer.DefaultPort;
        if (args.Length > 0 && (!int.TryParse(args[0], out port) || port <= 0 || port > 65535))
        {
            Logger.Log("Invalid port. Usage: telnet-start [port] (1-65535)", Logging.LogSeverity.Error);
            RequestStop();
            return;
        }

        Port = port;
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
/// The shell a Telnet client gets, running on the console session's thread.
/// </summary>
/// <remarks>
/// <see cref="Console"/> follows the thread: only the session's own thread writes to the client.
/// A command's code after an await may resume on another thread, so everything a command sends
/// to the terminal is queued, and the session thread carries it out while the command runs.
/// </remarks>
internal sealed class TelnetShell(ConsoleSession consoleSession) : ICommandConsole, ITextScreen
{
    private const string Prompt = "> ";
    private const char ControlC = '\x03';

    private readonly ConcurrentQueue<Action> terminalActions = new();
    private readonly ShellSession session = new(consoleSession.Name, canExit: true);
    private CommandRunner runner = null!;

    // While a full-screen program (ITextScreen) runs, every key goes to it.
    private readonly KeyQueue fullScreenKeys = new();
    private volatile bool fullScreen;

    // The client's window size, read on the session thread (Console follows the thread).
    private volatile int width = 80;
    private volatile int height = 24;

    /// <summary>Runs the shell until the client types <c>exit</c> or disconnects.</summary>
    public void Run()
    {
        runner = new CommandRunner(session, this);

        Console.WriteLine("Welcome to RemSox! (" + session.Name + ")");
        Console.WriteLine("Type 'help' to see available commands, 'exit' to disconnect.");

        while (!session.ExitRequested)
        {
            UpdateSize();
            Console.Write(Prompt);

            if (Console.ReadLine() is not { } line)
            {
                return;
            }

            Task command = runner.RunAsync(line);

            while (!command.IsCompleted)
            {
                RunTerminalActions();
                UpdateSize();

                if (fullScreen)
                {
                    ForwardKeys();
                }
                else
                {
                    CheckControlC();
                }

                Thread.Sleep(10);
            }

            fullScreen = false;
            fullScreenKeys.Close();

            RunTerminalActions();
        }
    }

    private void RunTerminalActions()
    {
        while (terminalActions.TryDequeue(out Action? action))
        {
            action();
        }
    }

    private void UpdateSize()
    {
        width = Console.WindowWidth;
        height = Console.WindowHeight;
    }

    private void ForwardKeys()
    {
        while (Console.KeyAvailable)
        {
            fullScreenKeys.Enqueue(Console.ReadKey(intercept: true));
        }
    }

    private void CheckControlC()
    {
        while (Console.KeyAvailable)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.KeyChar == ControlC || (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)))
            {
                runner.Cancel();
            }
        }
    }

    // --- ICommandConsole ---

    public void WriteLine(string text)
    {
        terminalActions.Enqueue(() => Console.WriteLine(text));
    }

    public void WriteError(string text)
    {
        terminalActions.Enqueue(() =>
        {
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(text);
            Console.ForegroundColor = previous;
        });
    }

    public void Clear()
    {
        terminalActions.Enqueue(Console.Clear);
    }

    public ITextScreen? Screen => this;

    // --- ITextScreen ---

    public int Width => width;

    public int Height => height;

    public void Begin()
    {
        terminalActions.Enqueue(() =>
        {
            fullScreen = true;
            Console.Clear();
        });
    }

    public void End()
    {
        terminalActions.Enqueue(() =>
        {
            fullScreen = false;
            fullScreenKeys.Close();
            Console.ResetColor();
            Console.Clear();
        });
    }

    public void WriteAt(int column, int row, string text, bool inverse = false)
    {
        terminalActions.Enqueue(() =>
        {
            // Never the last column: writing it moves the cursor on, and on the last row scrolls.
            int room = Console.WindowWidth - 1 - column;
            if (room <= 0 || row < 0 || row >= Console.WindowHeight)
            {
                return;
            }

            Console.SetCursorPosition(column, row);

            if (inverse)
            {
                Console.ForegroundColor = ConsoleColor.Black;
                Console.BackgroundColor = ConsoleColor.Gray;
            }

            Console.Write(text.Length > room ? text[..room] : text);

            if (inverse)
            {
                Console.ResetColor();
            }
        });
    }

    public void SetCursor(int column, int row)
    {
        terminalActions.Enqueue(() => Console.SetCursorPosition(Math.Clamp(column, 0, Console.WindowWidth - 1), Math.Clamp(row, 0, Console.WindowHeight - 1)));
    }

    public Task<ConsoleKeyInfo?> ReadKeyAsync(CancellationToken cancellation)
    {
        return fullScreenKeys.ReadAsync(cancellation);
    }

    public async Task<string?> ReadLineAsync(string prompt, CancellationToken cancellation)
    {
        TaskCompletionSource<string?> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        terminalActions.Enqueue(() =>
        {
            if (answer.Task.IsCompleted)
            {
                return;
            }

            Console.Write(prompt);
            _ = answer.TrySetResult(Console.ReadLine());
        });

        using (cancellation.Register(() => answer.TrySetResult(null)))
        {
            return await answer.Task;
        }
    }
}
