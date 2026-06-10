global using Sys = Cosmos.Kernel.System;

using RemSox.Processes;
using RemSox.Processing;
using RemSox.UI.CLI;
using RemSox.UI.CLI.Commands;

using System.Runtime;

namespace RemSox;

/// <summary>
/// Main kernel class - inherits from Cosmos.Kernel.System.Kernel.
/// </summary>
public class Kernel : Sys.Kernel
{
    protected override void BeforeRun()
    {
        CommandManager.RegisterCommands([
            new HelpCommand(),
            new ClearCommand(),
            new HaltCommand(),
            new SpawnProcessCommand(),
            new ListProcessesCommand(),
            new StopProcessCommand(),
            new StartGuiCommand(),
            new StopGuiCommand(),
            new ViewProcessLogs()
        ]);

        Sys.Graphics.Canvas canvas = Sys.Graphics.FullScreenCanvas.GetFullScreenCanvas();

        Sys.Mouse.MouseManager.Initialize();
        Sys.Mouse.MouseManager.SetScreenSize((int)canvas.Mode.Width, (int)canvas.Mode.Height);
        Sys.Keyboard.KeyboardManager.Initialize();
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
    }
}

public static unsafe partial class StartupCodeHelpers
{
    [RuntimeExport("fmod")]
    public static double fmod(double x, double y)
    {
        if (Math.Abs(y) < double.Epsilon)
        {
            return double.NaN;
        }

        double q = Math.Truncate(x / y);
        return x - (q * y);
    }
}