global using Sys = Cosmos.Kernel.System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime;
using System.Runtime.InteropServices;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core;
using Cosmos.Kernel.System.Graphics;
using RemSox.Processing;
using RemSox.Processing.IPC;
using RemSox.UI.GUI.CLI;
using RemSox.UI.GUI.CLI.Commands;
using RemSox.UI.GUI.Rendering;
using RemSox.UI.GUI.Windows;

namespace RemSox;

/// <summary>
/// Main kernel class - inherits from Cosmos.Kernel.System.Kernel.
/// </summary>
public class Kernel : Sys.Kernel
{
    protected override void BeforeRun()
    {
        Console.WriteLine("Cosmos booted successfully!");
        Console.WriteLine("Type a command to get it executed.");

        CommandManager.RegisterCommands(new ICommand[]
        {
            new HelpCommand(),
            new ClearCommand(),
            new HaltCommand(),
            new SpawnTestProcessCommand(),
            new ListProcessesCommand(),
            new StopProcessCommand(),
            new StartGuiCommand()
        });

        Sys.Mouse.MouseManager.Initialize();
        Sys.Keyboard.KeyboardManager.Initialize();

        for (int i = 0; i < 5; i++)
        {
            //ProcessManager.SpawnProcess<TestProcess>();
        }

        Console.WriteLine(CosmosFeatures.MouseEnabled);
        Console.WriteLine(CosmosFeatures.KeyboardEnabled);
    }

    protected override void Run()
    {
        if (Processes.DesktopProcess.IsRunning)
        {
            // Suspend the CLI while the GUI is active to prevent blocking and console corruption.
            Thread.Sleep(1000);
            return;
        }

        Console.Write("> ");

        string? input = Console.ReadLine();

        if (string.IsNullOrEmpty(input))
        {
            return;
        }

        if (CommandManager.TryExecute(input, line => Console.WriteLine(line)))
        {
            return;
        }

        Console.WriteLine($"\"{input}\" is not a command");
    }
}

public class TestProcess() : Processing.Process("Test Process")
{
    internal override void Run()
    {
        Window window = WindowManager.CreateWindow(this, "Test Window", Point.Empty, new Size(200, 150));
        window.AutoFlush = true;

        var circle = window.CreateUIElement<UI.GUI.UIEelements.Shapes.Circle>(rect =>
        {
            rect.Position = new Point(10, 10);
            rect.Radius = 50;
            rect.Color = Color.Red;
        });

        circle.Radius = 40;

        SendMessageToAllProcesses(new TestMessage { SenderProcessId = Id });

        while (!StopRequested)
        {
            Thread.Sleep(1000);
        }
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
            return double.NaN;

        double q = Math.Truncate(x / y);
        return x - q * y;
    }
}