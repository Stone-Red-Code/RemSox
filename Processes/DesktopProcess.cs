using System;
using System.Threading;
using Cosmos.Kernel.System.Graphics;
using RemSox.Processing;
using RemSox.UI.GUI.Rendering;
using RemSox.UI.GUI.Windows;

namespace RemSox.Processes;

public class DesktopProcess : Process
{
    private static bool isGraphicsInitialized = false;

    public static bool IsRunning { get; private set; } = false;

    public DesktopProcess() : base("Desktop Manager")
    {
    }

    internal override void Run()
    {
        IsRunning = true;

        if (!isGraphicsInitialized)
        {
            WindowManager.AddRenderSource(new CanvasRenderSource());
            isGraphicsInitialized = true;
        }

        // Trigger Canvas initialization
        FullScreenCanvas.GetFullScreenCanvas();

        // Force existing windows to redraw onto the new canvas renderer
        WindowManager.InvalidateAll();

        // Start the terminal process within the desktop environment
        ProcessManager.SpawnProcess<TerminalProcess>();

        // Create a test window for new UI controls
        Window testWindow = WindowManager.CreateWindow(this, "UI Controls Test", new System.Drawing.Point(500, 50), new System.Drawing.Size(200, 180));

        testWindow.CreateUIElement<RemSox.UI.GUI.UIEelements.Controls.Button>(b =>
        {
            b.Position = new System.Drawing.Point(20, 30);
            b.Size = new System.Drawing.Size(100, 30);
            b.Text = "Click Me";
            b.BackgroundColor = System.Drawing.Color.LightBlue;
        });

        testWindow.CreateUIElement<RemSox.UI.GUI.UIEelements.Controls.CheckBox>(c =>
        {
            c.Position = new System.Drawing.Point(20, 80);
            c.Text = "Check Me";
            c.IsChecked = true;
        });

        testWindow.CreateUIElement<RemSox.UI.GUI.UIEelements.Shapes.Line>(l =>
        {
            l.Position = new System.Drawing.Point(20, 130);
            l.EndPosition = new System.Drawing.Point(180, 130);
            l.Color = System.Drawing.Color.Red;
        });

        testWindow.AutoFlush = true;
        testWindow.Flush();

        while (!StopRequested)
        {
            WindowManager.Update();

            // Sleep slightly to yield CPU to the main CLI thread (approx 60 FPS)
            //Thread.Sleep(16);
        }

        IsRunning = false;

        // Cosmos doesn't robustly support switching back to text mode yet.
        // We will stop updating, but the screen will remain in graphics mode.
    }
}
