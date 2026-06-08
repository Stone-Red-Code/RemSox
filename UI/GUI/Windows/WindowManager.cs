using System;
using System.Drawing;
using RemSox.Processing;
using RemSox.UI.GUI.Rendering;

namespace RemSox.UI.GUI.Windows;

public static class WindowManager
{
    // Process ID to list of windows
    private static readonly Dictionary<int, List<Window>> windows = [];

    private static int nextWindowId = 1;

    private static Window? focusedWindow;

    private static readonly MuliRenderSource renderSource = new([]);

    public static void AddRenderSource(IRenderSource source) => renderSource.AddSource(source);

    public static void RemoveRenderSource(IRenderSource source) => renderSource.RemoveSource(source);

    public static Window CreateWindow(Process process, string title, Point position, Size size)
    {
        Window window = new(title, process.Id, GetNextWindowId(), renderSource)
        {
            Position = position,
            Size = size
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
        if (windows.ContainsKey(process.Id))
        {
            windows.Remove(process.Id);
        }
    }

    public static void FocusWindow(Window? window)
    {
        if (focusedWindow == window)
        {
            return;
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
