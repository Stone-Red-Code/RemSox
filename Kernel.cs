global using Sys = Cosmos.Kernel.System;

using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Network.IPv4.UDP.DHCP;
using Cosmos.Kernel.System.Timer;

using RemSox.Cryptography;
using RemSox.Processes;
using RemSox.Processing;
using RemSox.UI.CLI;
using RemSox.UI.CLI.Commands;

namespace RemSox;

/// <summary>
/// Main kernel class - inherits from Cosmos.Kernel.System.Kernel.
/// </summary>
public partial class Kernel : Sys.Kernel
{
    protected override void BeforeRun()
    {
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
            new StopRemoteDesktopCommand()
        ]);

        Console.WriteLine("Starting...");

        INetworkDevice? device = NetworkManager.PrimaryDevice;
        if (device == null)
        {
            Console.WriteLine("No network device.");
            return;
        }

        Console.WriteLine("Waiting for network link...");
        int attempts = 0;
        while (!device.LinkUp && attempts < 30)
        {
            TimerManager.Wait(100);
            attempts++;
        }

        if (!device.Ready)
        {
            Console.WriteLine("Network device not ready.");
            return;
        }

        Console.WriteLine("Initializing network stack...");
        NetworkStack.Initialize();

        Console.WriteLine("Requesting IP via DHCP...");
        DHCPClient dhcp = new DHCPClient();
        if (dhcp.SendDiscoverPacket() == -1)
        {
            Console.WriteLine("DHCP discover failed.");
            return;
        }

        IPConfig? config = NetworkConfigManager.Get(device);
        if (config?.IPAddress == null)
        {
            Console.WriteLine("Failed to obtain IP address.");
            return;
        }

        Console.WriteLine("IP: " + config.IPAddress);

        Sys.Graphics.Canvas canvas = Sys.Graphics.FullScreenCanvas.GetFullScreenCanvas();

        Sys.Mouse.MouseManager.Initialize();
        Sys.Mouse.MouseManager.SetScreenSize((int)canvas.Mode.Width, (int)canvas.Mode.Height);
        Sys.Keyboard.KeyboardManager.Initialize();
        _ = ProcessManager.SpawnProcess<CliProcess>();
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

        CryptoManager.Update();
        ProcessManager.TickAllProcesses();
    }
}

