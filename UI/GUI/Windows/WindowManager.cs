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

public static class WindowManager
{
    // Process ID to list of windows
    private static readonly ConcurrentDictionary<int, List<Window>> windows = new();

    private static int nextWindowId = 1;
    private static int nextZIndex = 1;

    private static Window? focusedWindow;

    private static Window? activeInteractWindow = null;
    private static Point lastPointerPosition = Point.Empty;
    private static bool wasLeftButtonDown = false;
    private static int mousePollCounter = 0;

    private static readonly MuliRenderSource renderSource = new([]);

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

        if (KeyboardManager.TryReadKey(out KeyEvent keyEvent))
        {
            focusedWindow?.HandleKeyEvent(keyEvent);
        }

        Canvas canvas = FullScreenCanvas.GetFullScreenCanvas();
        CanvasRenderSource.CompositeAndDisplay(canvas, pointerPosition);

        lastPointerPosition = pointerPosition;
    }

    public static void AddRenderSource(IRenderSource source) => renderSource.AddSource(source);

    public static void RemoveRenderSource(IRenderSource source) => renderSource.RemoveSource(source);

    public static Window CreateWindow(Process process, string title, Point position, Size size)
    {
        Window window = new(title, process.Id, GetNextWindowId(), renderSource)
        {
            Position = position,
            Size = size,
            ZIndex = nextZIndex++
        };

        if (!windows.ContainsKey(process.Id))
        {
            windows[process.Id] = [];
        }

        windows[process.Id].Add(window);

        return window;
    }

    public static void CloseWindow(Window window)
    {
        if (windows.TryGetValue(window.ProcessId, out var processWindows))
        {
            processWindows.Remove(window);
        }
        
        renderSource.Render(new[] { new RenderCommand { WindowId = window.Id, ElementId = window.Id, ElementType = "WindowClose", Position = window.Position, Properties = new Dictionary<string, object?>() } });
    }

    public static List<Window> GetWindowsForProcess(Process process)
    {
        if (windows.TryGetValue(process.Id, out var processWindows))
        {
            return processWindows;
        }

        return [];
    }

    public static void CloseWindowsForProcess(Process process)
    {
        windows.TryRemove(process.Id, out _);
    }

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

    public static bool IsWindowFocused(Window window)
    {
        return focusedWindow == window;
    }

    public static Window? TryBeginInteract(Point pointerPosition)
    {
        var allWindows = windows.Values.SelectMany(w => w).OrderByDescending(w => w.ZIndex);
        foreach (var window in allWindows)
        {
            if (window.TryBeginInteract(pointerPosition))
            {
                return window;
            }
        }
        return null;
    }

    public static void InvalidateAll()
    {
        foreach (var processWindows in windows.Values)
        {
            foreach (var window in processWindows)
            {
                window.Invalidate();
            }
        }
    }

    private static int GetNextWindowId()
    {
        return nextWindowId++;
    }

    sealed private class MuliRenderSource(List<IRenderSource> sources) : IRenderSource
    {
        public void AddSource(IRenderSource source) => sources.Add(source);

        public void RemoveSource(IRenderSource source) => sources.Remove(source);

        public void Render(IEnumerable<RenderCommand> commands)
        {
            foreach (IRenderSource source in sources)
            {
                source.Render(commands);
            }
        }
    }
}
