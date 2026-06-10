using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;

using RemSox.Processing;
using RemSox.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.UI.GUI.Windows;

/// <summary>
/// Manages window creation, focus, interaction, and rendering orchestration for the GUI system.
/// </summary>
public static class WindowManager
{
    private static readonly Lock windowsLock = new();
    // Process ID to list of windows
    private static readonly Dictionary<int, List<Window>> windows = [];

    private static int nextWindowId = 1;
    private static int nextZIndex = 1;

    private static Window? focusedWindow;

    private static Window? activeInteractWindow = null;
    private static Point lastPointerPosition = Point.Empty;
    private static bool wasLeftButtonDown = false;

    private static readonly MuliRenderSource renderSource = new([]);

    /// <summary>
    /// Processes input, updates interaction state, and triggers rendering of all windows.
    /// </summary>
    public static void Update()
    {
        Point pointerPosition = new(MouseManager.X, MouseManager.Y);
        bool leftButtonDown = MouseManager.LeftButton;

        Canvas canvas = FullScreenCanvas.GetFullScreenCanvas();

        if (leftButtonDown && !wasLeftButtonDown)
        {
            activeInteractWindow = TryBeginInteract(pointerPosition);
        }
        else if (leftButtonDown && activeInteractWindow is not null)
        {
            activeInteractWindow.UpdateInteraction(pointerPosition, new Point((int)canvas.Mode.Width, (int)canvas.Mode.Height));
        }
        else if (!leftButtonDown && activeInteractWindow is not null)
        {
            activeInteractWindow.EndInteraction();
            activeInteractWindow = null;
        }

        wasLeftButtonDown = leftButtonDown;

        while (KeyboardManager.TryReadKey(out KeyEvent? keyEvent) && keyEvent is not null)
        {
            focusedWindow?.HandleKeyEvent(keyEvent);
        }

        CanvasRenderSource.CompositeAndDisplay(canvas, pointerPosition);

        lastPointerPosition = pointerPosition;
    }

    /// <summary>
    /// Adds a new rendering source to the compositor.
    /// </summary>
    public static void AddRenderSource(IRenderSource source)
    {
        renderSource.AddSource(source);
    }

    /// <summary>
    /// Removes an existing rendering source from the compositor.
    /// </summary>
    public static void RemoveRenderSource(IRenderSource source)
    {
        renderSource.RemoveSource(source);
    }

    /// <summary>
    /// Creates and registers a new window for the specified process.
    /// </summary>
    public static Window CreateWindow(Process process, string title, Point position, Size size)
    {
        Window window = new(title, process.Id, GetNextWindowId(), renderSource)
        {
            Position = position,
            Size = size,
            ZIndex = nextZIndex++
        };

        lock (windowsLock)
        {
            if (!windows.ContainsKey(process.Id))
            {
                windows[process.Id] = [];
            }

            windows[process.Id].Add(window);
        }

        return window;
    }

    /// <summary>
    /// Closes a specific window and notifies the renderer.
    /// </summary>
    public static void CloseWindow(Window window)
    {
        lock (windowsLock)
        {
            if (windows.TryGetValue(window.ProcessId, out List<Window>? processWindows))
            {
                _ = processWindows.Remove(window);
            }
        }

        if (focusedWindow == window)
        {
            focusedWindow = null;
        }
        if (activeInteractWindow == window)
        {
            activeInteractWindow = null;
        }

        renderSource.Render([new RenderCommand { WindowId = window.Id, ElementId = window.Id, ElementType = "WindowClose", Position = window.Position, Properties = new Dictionary<string, object?>() }]);
    }

    /// <summary>
    /// Returns a list of all windows belonging to the given process.
    /// </summary>
    public static List<Window> GetWindowsForProcess(Process process)
    {
        lock (windowsLock)
        {
            if (windows.TryGetValue(process.Id, out List<Window>? processWindows))
            {
                return processWindows.ToList();
            }

            return [];
        }
    }

    /// <summary>
    /// Closes all windows belonging to the given process.
    /// </summary>
    public static void CloseWindowsForProcess(Process process)
    {
        CloseWindowsForProcess(process.Id);
    }

    /// <summary>
    /// Closes all windows belonging to the given process ID.
    /// </summary>
    public static void CloseWindowsForProcess(int processId)
    {
        List<Window> windowsToClose = [];
        lock (windowsLock)
        {
            if (windows.TryGetValue(processId, out List<Window>? processWindows))
            {
                windowsToClose.AddRange(processWindows);
                _ = windows.Remove(processId);
            }
        }

        if (windowsToClose.Count > 0)
        {
            if (focusedWindow != null && windowsToClose.Contains(focusedWindow))
            {
                focusedWindow = null;
            }
            if (activeInteractWindow != null && windowsToClose.Contains(activeInteractWindow))
            {
                activeInteractWindow = null;
            }

            List<RenderCommand> closeCommands = [];
            foreach (Window window in windowsToClose)
            {
                closeCommands.Add(new RenderCommand { WindowId = window.Id, ElementId = window.Id, ElementType = "WindowClose", Position = window.Position, Properties = new Dictionary<string, object?>() });
            }
            renderSource.Render(closeCommands);
        }
    }

    /// <summary>
    /// Sets the focus to the specified window, bringing it to the foreground.
    /// </summary>
    public static void FocusWindow(Window? window)
    {
        if (focusedWindow == window)
        {
            return;
        }

        _ = window?.ZIndex = nextZIndex++;

        Window? previousFocusedWindow = focusedWindow;
        focusedWindow = window;

        previousFocusedWindow?.Flush();
        focusedWindow?.Flush();
    }

    /// <summary>
    /// Checks if the specified window is currently focused.
    /// </summary>
    public static bool IsWindowFocused(Window window)
    {
        return focusedWindow == window;
    }

    /// <summary>
    /// Attempts to begin interaction (drag/resize) with a window at the specified pointer position.
    /// </summary>
    public static Window? TryBeginInteract(Point pointerPosition)
    {
        List<Window> allWindows;
        lock (windowsLock)
        {
            allWindows = windows.Values.SelectMany(w => w).OrderByDescending(w => w.ZIndex).ToList();
        }

        foreach (Window window in allWindows)
        {
            if (window.TryBeginInteract(pointerPosition))
            {
                return window;
            }
        }
        return null;
    }

    /// <summary>
    /// Forces a full redraw of all windows in the system.
    /// </summary>
    public static void InvalidateAll()
    {
        List<Window> allWindows;
        lock (windowsLock)
        {
            allWindows = windows.Values.SelectMany(w => w).ToList();
        }

        foreach (Window window in allWindows)
        {
            window.Invalidate();
        }
    }

    private static int GetNextWindowId()
    {
        return nextWindowId++;
    }

    private sealed class MuliRenderSource(List<IRenderSource> sources) : IRenderSource
    {
        private readonly Lock sourcesLock = new();

        public void AddSource(IRenderSource source)
        {
            lock (sourcesLock)
            {
                sources.Add(source);
            }
        }

        public void RemoveSource(IRenderSource source)
        {
            lock (sourcesLock)
            {
                _ = sources.Remove(source);
            }
        }

        public void Render(IEnumerable<RenderCommand> commands)
        {
            List<IRenderSource> sourcesCopy;
            lock (sourcesLock)
            {
                sourcesCopy = sources.ToList();
            }

            foreach (IRenderSource source in sourcesCopy)
            {
                source.Render(commands);
            }
        }
    }
}
