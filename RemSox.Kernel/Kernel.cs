global using Sys = Cosmos.Kernel.System;

using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Timers;

using RemSox.Kernel.Accounts;
using RemSox.Kernel.Configuration;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;
using RemSox.Kernel.Processes;
using RemSox.Kernel.Processing;
using RemSox.Kernel.Programs;
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
        MainLoop.Install();
        SystemLog.Log("RemSox is starting.", LogSeverity.Info);

        CommandManager.RegisterCommands([
            new HelpCommand(),
            new ClearCommand(),
            new ShutdownCommand(),
            new RebootCommand(),
            new ListProcessesCommand(),
            new StopProcessCommand(),
            new LogsCommand(),
            new BeepCommand(),
            new PlayCommand(),
            new StopPlaybackCommand(),
            new ExitCommand(),
            new ConfigCommand(),
            new EditCommand(),
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
            new DiskFreeCommand(),
            new WhoAmICommand(),
            new WhoCommand(),
            new UsersCommand(),
            new AddUserCommand(),
            new DeleteUserCommand(),
            new PasswordCommand(),
            new LogoutCommand()
        ]);
        BuiltInPrograms.Register();

        Console.WriteLine("Starting...");

        FileSystemManager.Initialize();
        if (!FileSystemManager.IsMounted)
        {
            SystemLog.Log("No disk is mounted: settings use their defaults and nothing can be saved.", LogSeverity.Warning);
        }

        _ = SystemSettings.Load();
        AccountStore.Load();
        CreateStartupScript();

        Sys.Graphics.Canvas canvas = Sys.Graphics.Canvas.GetFullScreen();
        Sys.Input.MouseManager.SetScreenSize((int)canvas.Mode.Width, (int)canvas.Mode.Height);

        InitializeNetwork();

        ShowLoginScreen();
        RunStartupScript();
    }

    /// <summary>
    /// Runs the startup script without a Terminal, as <c>noterminal /system/startup.rsh</c> would,
    /// in the system's session (so it can start services but nothing that needs a user). Its
    /// errors also go to the console so a broken script does not fail unnoticed, unless the
    /// login window has the screen: console text would be drawn over it.
    /// </summary>
    private static void RunStartupScript()
    {
        if (!File.Exists(SystemPaths.StartupScript))
        {
            return;
        }

        if (ProcessManager.SpawnProcess<NoTerminalProcess>([SystemPaths.StartupScript]) is { } id
            && ProcessManager.GetProcess(id) is NoTerminalProcess process)
        {
            process.EchoErrorsToConsole = ProcessManager.IsProcessRunning<CliProcess>();
        }
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
            SystemLog.Log($"Could not create {SystemPaths.StartupScript}: {ex.Message}", LogSeverity.Error);
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

    /// <summary>
    /// Gives the screen to the login: the console prompt, or the login window with
    /// <c>login.screen = graphical</c> (which needs a disk to have accounts on). First ends the
    /// console sessions nothing holds any more, such as that of a desktop that just stopped.
    /// </summary>
    private static void ShowLoginScreen()
    {
        LogonSessions.EndIdleConsoleSessions();

        if (SystemSettings.Get(SystemSettings.LoginScreenKey) == SystemSettings.GraphicalLogin && AccountStore.IsAvailable)
        {
            _ = ProcessManager.SpawnProcess<LoginWindowProcess>();
        }
        else
        {
            _ = ProcessManager.SpawnProcess<CliProcess>();
        }
    }

    protected override void Run()
    {
        // Whatever has the screen: the desktop or the login window in graphics mode, else the console.
        bool desktopRunning = ProcessManager.IsProcessRunning<DesktopProcess>() || ProcessManager.IsProcessRunning<LoginWindowProcess>();
        bool cliRunning = ProcessManager.IsProcessRunning<CliProcess>();

        if (!desktopRunning && !cliRunning)
        {
            ShowLoginScreen();
            return;
        }

        if (desktopRunning && cliRunning)
        {
            foreach (Process process in ProcessManager.GetProcessesOfType<CliProcess>())
            {
                ProcessManager.StopProcess(process.Id);
            }
        }

        MainLoop.RunOnce();
    }
}
