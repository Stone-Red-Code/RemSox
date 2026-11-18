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
internal sealed class TelnetShell(ConsoleSession consoleSession) : ICommandConsole
{
    private const string Prompt = "> ";
    private const char ControlC = '\x03';

    private readonly ConcurrentQueue<Action> terminalActions = new();
    private readonly ShellSession session = new(consoleSession.Name, canExit: true);
    private CommandRunner runner = null!;

    /// <summary>Runs the shell until the client types <c>exit</c> or disconnects.</summary>
    public void Run()
    {
        runner = new CommandRunner(session, this);

        Console.WriteLine("Welcome to RemSox! (" + session.Name + ")");
        Console.WriteLine("Type 'help' to see available commands, 'exit' to disconnect.");

        while (!session.ExitRequested)
        {
            Console.Write(Prompt);

            if (Console.ReadLine() is not { } line)
            {
                return;
            }

            Task command = runner.RunAsync(line);

            while (!command.IsCompleted)
            {
                RunTerminalActions();
                CheckControlC();
                Thread.Sleep(10);
            }

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
