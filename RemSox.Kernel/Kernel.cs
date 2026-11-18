global using Sys = Cosmos.Kernel.System;

using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Timers;

using RemSox.Kernel.Configuration;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;
using RemSox.Kernel.Processes;
using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;
using RemSox.Kernel.UI.CLI.Commands;

namespace RemSox.Kernel;

/// <summary>
/// Main kernel class - inherits from Cosmos.Kernel.System.Kernel.
/// </summary>
public partial class Kernel : Sys.Kernel
{
    protected override void BeforeRun()
    {
        SystemLog.Log("RemSox is starting.", LogSeverity.Info);

        CommandManager.RegisterCommands([
            new HelpCommand(),
            new ClearCommand(),
            new ShutdownCommand(),
            new RebootCommand(),
            new SpawnProcessCommand(),
            new ListProcessesCommand(),
            new StopProcessCommand(),
            new StartGuiCommand(),
            new StopGuiCommand(),
            new ViewProcessLogs(),
            new YesNtCommand(),
            new StartRemoteDesktopCommand(),
            new StopRemoteDesktopCommand(),
            new StartTelnetCommand(),
            new StopTelnetCommand(),
            new BeepCommand(),
            new PlayCommand(),
            new StopPlaybackCommand(),
            new ExitCommand(),
            new ConfigCommand(),
            new AutostartCommand(),
            new RunScriptCommand(),
            new ListDirectoryCommand(),
            new ChangeDirectoryCommand(),
            new PrintWorkingDirectoryCommand(),
            new CatCommand(),
            new TouchCommand(),
            new WriteFileCommand(),
            new MakeDirectoryCommand(),
            new RemoveCommand(),
            new CopyCommand(),
            new MoveCommand(),
            new DiskFreeCommand()
        ]);

        Console.WriteLine("Starting...");

        FileSystemManager.Initialize();
        SystemSettings.Load();
        CreateStartupScript();

        Sys.Graphics.Canvas canvas = Sys.Graphics.Canvas.GetFullScreen();
        Sys.Input.MouseManager.SetScreenSize((int)canvas.Mode.Width, (int)canvas.Mode.Height);

        InitializeNetwork();

        _ = ProcessManager.SpawnProcess<CliProcess>();
        _ = ProcessManager.SpawnProcess<AutostartProcess>();
    }

    /// <summary>Creates the startup script with commented examples on a disk that has none yet.</summary>
    private static void CreateStartupScript()
    {
        try
        {
            ShellScript.EnsureInitScript();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SystemLog.Log($"Could not create {ShellScript.InitScriptPath}: {ex.Message}", LogSeverity.Error);
        }
    }

    /// <summary>Waits for the network link and configures it by DHCP; problems are reported and skipped.</summary>
    private static void InitializeNetwork()
    {
        NetworkAdapter adapter = NetworkManager.Primary;
        if (!adapter.IsValid)
        {
            Console.WriteLine("No network device.");
            return;
        }

        Console.WriteLine("Waiting for network link...");
        int attempts = 0;
        while (!adapter.LinkUp && attempts < 30)
        {
            TimerManager.Wait(100);
            attempts++;
        }

        if (!adapter.Ready)
        {
            Console.WriteLine("Network device not ready.");
            return;
        }

        Console.WriteLine("Requesting IP via DHCP...");
        DhcpClient dhcp = new();
        if (dhcp.SendDiscoverPacket() == -1)
        {
            Console.WriteLine("DHCP discover failed.");
            return;
        }

        IPConfig? config = adapter.IPConfig;
        if (config == null)
        {
            Console.WriteLine("Failed to obtain IP address.");
            return;
        }

        Console.WriteLine("IP: " + config.Address);
        SystemLog.Log("Network configured: " + config.Address, LogSeverity.Info);
    }

    protected override void Run()
    {
        bool desktopRunning = ProcessManager.IsProcessRunning<DesktopProcess>();
        bool cliRunning = ProcessManager.IsProcessRunning<CliProcess>();

        if (!desktopRunning && !cliRunning)
        {
            _ = ProcessManager.SpawnProcess<CliProcess>();
            return;
        }

        if (desktopRunning && cliRunning)
        {
            foreach (Process process in ProcessManager.GetProcessesOfType<CliProcess>())
            {
                ProcessManager.StopProcess(process.Id);
            }
        }

        ProcessManager.TickAllProcesses();
        SystemLog.FlushIfDue();
    }
}
