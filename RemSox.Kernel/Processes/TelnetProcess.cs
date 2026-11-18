using System.Collections.Concurrent;

using Cosmos.Kernel.System.Sessions;
using Cosmos.Network.Telnet;

using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Processes;

/// <summary>
/// Serves the RemSox command line over Telnet. Every client gets a console session of its own
/// (listed next to the local one and reachable with Alt+Fn), running the same commands as the CLI.
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
        server = new TelnetServer(RunShell, (ushort)port)
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

    /// <summary>
    /// The shell a Telnet client gets. It runs on the session's thread, whose <see cref="Console"/>
    /// is the client's terminal, and returns (hanging up) on "exit" or when the client disconnects.
    /// </summary>
    private static void RunShell(ConsoleSession session)
    {
        Console.WriteLine("Welcome to RemSox! (" + session.Name + ")");
        Console.WriteLine("Type 'help' to see available commands, 'exit' to disconnect.");

        while (true)
        {
            Console.Write("> ");

            if (Console.ReadLine() is not { } line || line.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(line) && !RunCommand(line))
            {
                Console.WriteLine($"\"{line}\" is not a command");
            }
        }
    }

    /// <summary>
    /// Runs a command and writes its output to the session.
    /// </summary>
    /// <remarks>
    /// A command's code after an await may continue on another thread, whose Console is the local
    /// display rather than this session. Output is therefore queued and written by the session's
    /// own thread while it waits for the command to finish.
    /// </remarks>
    private static bool RunCommand(string line)
    {
        ConcurrentQueue<string> output = new();
        Task<bool> command = CommandManager.TryExecuteAsync(line, output.Enqueue);

        while (!command.IsCompleted)
        {
            WriteQueued(output);
            Thread.Sleep(10);
        }

        WriteQueued(output);

        try
        {
            return command.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Command failed: " + ex.Message);
            return true;
        }
    }

    private static void WriteQueued(ConcurrentQueue<string> output)
    {
        while (output.TryDequeue(out string? text))
        {
            Console.WriteLine(text);
        }
    }
}
