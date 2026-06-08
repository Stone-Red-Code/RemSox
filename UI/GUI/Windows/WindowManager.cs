using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Mouse;
using Cosmos.Kernel.System.Keyboard;
using RemSox.Processing;
using RemSox.UI.GUI.Rendering;

namespace RemSox.UI.GUI.Windows;

/// <summary>
/// Manages window creation, focus, interaction, and rendering orchestration for the GUI system.
/// </summary>
public static class WindowManager
{
    private static readonly object windowsLock = new object();
    // Process ID to list of windows
    private static readonly Dictionary<int, List<Window>> windows = new();

    private static int nextWindowId = 1;
    private static int nextZIndex = 1;

    private static Window? focusedWindow;

    private static Window? activeInteractWindow = null;
    private static Point lastPointerPosition = Point.Empty;
    private static bool wasLeftButtonDown = false;
    private static int mousePollCounter = 0;

    private static readonly MuliRenderSource renderSource = new([]);

    /// <summary>
    /// Processes input, updates interaction state, and triggers rendering of all windows.
    /// </summary>
    public static void Update()
    {
        mousePollCounter++;
        MouseManager.Poll();

        Point pointerPosition = new((int)MouseManager.X, (int)MouseManager.Y);
        bool leftButtonDown = MouseManager.LeftButton;

        if (leftButtonDown && !wasLeftButtonDown)
        {
            activeInteractWindow = TryBeginInteract(pointerPosition);
        }
        else if (leftButtonDown && activeInteractWindow is not null)
        {
            activeInteractWindow.UpdateInteraction(pointerPosition);
        }
        else if (!leftButtonDown && activeInteractWindow is not null)
        {
            activeInteractWindow.EndInteraction();
            activeInteractWindow = null;
        }

        wasLeftButtonDown = leftButtonDown;

        while (KeyboardManager.TryReadKey(out KeyEvent keyEvent))
        {
            focusedWindow?.HandleKeyEvent(keyEvent);
        }

        Canvas canvas = FullScreenCanvas.GetFullScreenCanvas();
        CanvasRenderSource.CompositeAndDisplay(canvas, pointerPosition);

        lastPointerPosition = pointerPosition;
    }

    /// <summary>
    /// Adds a new rendering source to the compositor.
    /// </summary>
    public static void AddRenderSource(IRenderSource source) => renderSource.AddSource(source);

    /// <summary>
    /// Removes an existing rendering source from the compositor.
    /// </summary>
    public static void RemoveRenderSource(IRenderSource source) => renderSource.RemoveSource(source);

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
            if (windows.TryGetValue(window.ProcessId, out var processWindows))
            {
                processWindows.Remove(window);
            }
        }

        renderSource.Render(new[] { new RenderCommand { WindowId = window.Id, ElementId = window.Id, ElementType = "WindowClose", Position = window.Position, Properties = new Dictionary<string, object?>() } });
    }

    /// <summary>
    /// Returns a list of all windows belonging to the given process.
    /// </summary>
    public static List<Window> GetWindowsForProcess(Process process)
    {
        lock (windowsLock)
        {
            if (windows.TryGetValue(process.Id, out var processWindows))
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
        List<Window> windowsToClose = new();
        lock (windowsLock)
        {
            if (windows.TryGetValue(processId, out var processWindows))
            {
                windowsToClose.AddRange(processWindows);
                windows.Remove(processId);
            }
        }

        if (windowsToClose.Count > 0)
        {
            List<RenderCommand> closeCommands = new();
            foreach (var window in windowsToClose)
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

        if (window != null)
        {
            window.ZIndex = nextZIndex++;
        }

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

        foreach (var window in allWindows)
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

        foreach (var window in allWindows)
        {
            window.Invalidate();
        }
    }

    private static int GetNextWindowId()
    {
        return nextWindowId++;
    }

    sealed private class MuliRenderSource(List<IRenderSource> sources) : IRenderSource
    {
        private readonly object sourcesLock = new object();

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
                sources.Remove(source);
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
