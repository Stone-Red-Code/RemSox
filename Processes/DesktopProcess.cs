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

        while (!StopRequested)
        {
            WindowManager.Update();
            
            // Sleep slightly to yield CPU to the main CLI thread (approx 60 FPS)
            Thread.Sleep(16);
        }
        
        IsRunning = false;
        
        // Cosmos doesn't robustly support switching back to text mode yet.
        // We will stop updating, but the screen will remain in graphics mode.
    }
}
