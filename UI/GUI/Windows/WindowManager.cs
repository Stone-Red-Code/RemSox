using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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

    private static readonly MuliRenderSource renderSource = new([]);

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
