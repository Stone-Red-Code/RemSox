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
using RemSox.Kernel.Security;
using RemSox.Kernel.UI.CLI;
using RemSox.Kernel.UI.CLI.Commands;
using RemSox.Kernel.UI.GUI;
using RemSox.Kernel.UI.GUI.Windows;

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
            new SessionCommand(),
            new UserCommand(),
            new LogoutCommand(),
            new LockCommand(),
            new AdminCommand(),
            new PermissionCommand(),
            new GroupCommand(),
            new ServiceCommand()
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
        PermissionRules.Load();
        ServiceList.Load();
        LogonSessions.Started += UserStartup.Run;
        // The keyboard layout is the account's at the machine while someone is logged in there.
        LogonSessions.MachineChanged += SystemSettings.ApplyKeyboardLayout;
        LogonSessions.Ended += Screen.Remove;

        Sys.Graphics.Canvas canvas = Sys.Graphics.Canvas.GetFullScreen();
        Sys.Input.MouseManager.SetScreenSize((int)canvas.Mode.Width, (int)canvas.Mode.Height);

        InitializeNetwork();

        // A service that failed shows at the console's login prompt as well as in the log.
        CliProcess.BootErrors.AddRange(ServiceList.StartAll());

        ShowLoginScreen();
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
    /// session at the machine if nothing holds it any more, such as a console shell's that crashed.
    /// </summary>
    private static void ShowLoginScreen()
    {
        LogonSessions.EndIdleSessionAtMachine();

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
        // What the machine's display shows: the desktop of whoever is logged in at the machine,
        // or the login window, in graphics mode; else the console. Desktops of remote desktop
        // sessions have screens of their own and do not count.
        Process? graphical = ProcessManager.GetProcessesOfType<DesktopProcess>().FirstOrDefault(desktop => desktop.IsRunning && desktop.Session is { IsAtMachine: true, HasEnded: false })
            ?? (Process?)ProcessManager.GetProcessesOfType<LoginWindowProcess>().FirstOrDefault(login => login.IsRunning);
        bool cliRunning = ProcessManager.IsProcessRunning<CliProcess>();

        if (graphical is null && !cliRunning)
        {
            LocalDisplay.Show(null);
            ShowLoginScreen();
            return;
        }

        if (graphical is not null && cliRunning)
        {
            foreach (Process process in ProcessManager.GetProcessesOfType<CliProcess>())
            {
                ProcessManager.StopProcess(process.Id);
            }
        }

        LocalDisplay.Show(graphical?.Screen);
        LocalDisplay.PollInput();
        Screen.UpdateAll();
        MainLoop.RunOnce();
    }
}
