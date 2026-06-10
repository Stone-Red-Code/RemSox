global using Sys = Cosmos.Kernel.System;

using RemSox.Processes;
using RemSox.Processing;
using RemSox.Processing.IPC;
using RemSox.UI.CLI;
using RemSox.UI.CLI.Commands;
using RemSox.UI.GUI.UIEelements.Shapes;
using RemSox.UI.GUI.Windows;

using System.Drawing;
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
            new SpawnTestProcessCommand(),
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

public class TestProcess() : Process("Test Process")
{
    internal override void Start(string[] args)
    {
        Window window = WindowManager.CreateWindow(this, "Test Window", Point.Empty, new Size(200, 150));
        window.AutoFlush = true;

        Circle circle = window.CreateUIElement<UI.GUI.UIEelements.Shapes.Circle>(rect =>
        {
            rect.Position = new Point(10, 10);
            rect.Radius = 50;
            rect.Color = Color.Red;
        });

        circle.Radius = 40;

        SendMessageToAllProcesses(new TestMessage { SenderProcessId = Id });
    }

    internal override void Tick()
    {
        // Example tick logic - could be used for animations, timed events, etc.
    }

    internal override void HandleInterProcessMessage(Message message)
    {
        if (message is TestMessage testMessage && message.SenderProcessId != Id)
        {
            Console.WriteLine($"Received message in {Name} (ID: {Id}): {testMessage.MessageText}");
        }
    }
}

internal class TestMessage : Message
{
    public string MessageText { get; set; } = "Hello from TestMessage!";
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