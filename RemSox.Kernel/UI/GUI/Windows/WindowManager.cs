using RemSox.Shared.UI;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;

using RemSox.Kernel.Processing;
using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.Windows;

/// <summary>
/// Manages window creation, focus, interaction, and rendering orchestration for the GUI system.
/// </summary>
public static class WindowManager
{
    private static readonly List<Window> windows = [];

    private static int nextWindowId = 1;
    private static int nextZIndex = 1;

    private static Window? focusedWindow;

    private static Window? activeInteractWindow = null;
    private static Point pointerPosition = Point.Empty;

    // Last cursor position sent to the render sources; null forces a resend (e.g. after a remote sync).
    private static Point? sentCursorPosition;
    private static bool leftButtonDown = false;

    private static readonly MultiRenderSource renderSource = new([]);

    private static readonly Queue<MouseEvent> mouseQueue = [];
    private static readonly Queue<KeyEvent> keyQueue = [];

    /// <summary> Enqueues a mouse event from any input source. </summary>
    public static void EnqueueMouseEvent(MouseEvent mouseEvent)
    {
        mouseQueue.Enqueue(mouseEvent);
    }

    /// <summary> Enqueues a keyboard event from any input source. </summary>
    public static void EnqueueKeyEvent(KeyEvent keyEvent)
    {
        keyQueue.Enqueue(keyEvent);
    }
    /// <summary>
    /// Handles the input queued since the last tick, in the order it arrived, and composites the
    /// screen.
    /// </summary>
    internal static void Update()
    {
        List<MouseEvent> mouseEvents;
        List<KeyEvent> keyEvents;

        mouseEvents = [.. mouseQueue];
        mouseQueue.Clear();
        keyEvents = [.. keyQueue];
        keyQueue.Clear();

        foreach (KeyEvent keyEvent in keyEvents)
        {
            focusedWindow?.HandleKeyEvent(keyEvent);
        }

        for (int i = 0; i < mouseEvents.Count; i++)
        {
            // Only the last of several moves in a row matters; the others would only drag a
            // window through positions nobody sees.
            bool superseded = mouseEvents[i].Type == MouseEventType.Move && i + 1 < mouseEvents.Count && mouseEvents[i + 1].Type == MouseEventType.Move;
            if (!superseded)
            {
                HandleMouseEvent(mouseEvents[i]);
            }
        }

        // Only send the cursor when it moved: each SetCursor is a network round trip for remote
        // viewers and marks the local canvas dirty.
        if (sentCursorPosition != pointerPosition)
        {
            renderSource.Render([new RenderCommand
            {
                Type = RenderCommandType.SetCursor,
                WindowId = 0,
                ElementId = 0,
                Position = pointerPosition,
            }]);
            sentCursorPosition = pointerPosition;
        }

        renderSource.Composite();
    }

    /// <summary>
    /// Passes one mouse event on, so a press and release that arrive in the same tick are still
    /// a click. Mouse events go to the window a left press landed on until the button is released
    /// (so a foreground window such as the taskbar gets its clicks without taking keyboard focus,
    /// and a window being dragged keeps following), and to the focused window otherwise.
    /// </summary>
    private static void HandleMouseEvent(MouseEvent mouseEvent)
    {
        Point previousPosition = pointerPosition;
        pointerPosition = new Point(mouseEvent.X, mouseEvent.Y);

        bool isLeft = mouseEvent.Button == MouseButton.Left;
        Window? chromeClickWindow = null;
        Window.ChromeButton chromeClick = Window.ChromeButton.None;

        switch (mouseEvent.Type)
        {
            case MouseEventType.Move:
                if (pointerPosition == previousPosition)
                {
                    return;
                }

                if (leftButtonDown && activeInteractWindow is not null)
                {
                    activeInteractWindow.UpdateInteraction(pointerPosition, GetScreenBounds().Size);
                }
                break;

            case MouseEventType.ButtonDown when isLeft:
                if (leftButtonDown)
                {
                    return;
                }

                leftButtonDown = true;
                activeInteractWindow = TryBeginInteract(pointerPosition);
                break;

            case MouseEventType.ButtonUp when isLeft:
                if (!leftButtonDown)
                {
                    return;
                }

                leftButtonDown = false;
                if (activeInteractWindow is not null)
                {
                    chromeClickWindow = activeInteractWindow;
                    chromeClick = activeInteractWindow.EndInteraction(pointerPosition);
                }
                break;
        }

        Window? target = activeInteractWindow ?? focusedWindow;
        target?.HandleMouseEvent(mouseEvent);

        if (mouseEvent.Type == MouseEventType.ButtonUp && isLeft)
        {
            activeInteractWindow = null;
        }

        // Title bar button actions run last: closing a window can stop its process.
        switch (chromeClick)
        {
            case Window.ChromeButton.Close:
                RequestCloseWindow(chromeClickWindow!);
                break;
            case Window.ChromeButton.Maximize:
                ToggleMaximizeWindow(chromeClickWindow!);
                break;
            case Window.ChromeButton.Minimize:
                MinimizeWindow(chromeClickWindow!);
                break;
        }
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
    /// The part of the screen maximized windows fill, e.g. the screen above the taskbar;
    /// null means the whole screen.
    /// </summary>
    public static Rectangle? WorkArea { get; set; }

    /// <summary>
    /// Creates and registers a new window for the specified process. The window takes keyboard
    /// focus unless <paramref name="activate"/> is false.
    /// </summary>
    public static Window CreateWindow(Process process, string title, Size size, Point position, bool activate = true)
    {
        MainLoop.AssertMainThread(nameof(CreateWindow));

        Window window = new(title, process.Id, GetNextWindowId(), renderSource)
        {
            Position = position,
            Size = size,
            ZIndex = nextZIndex++
        };

        windows.Add(window);

        if (activate)
        {
            FocusWindow(window);
        }

        return window;
    }

    /// <summary>
    /// Creates and registers a new window, automatically finding the most spacious, least overlapping position.
    /// The window takes keyboard focus.
    /// </summary>
    public static Window CreateWindow(Process process, string title, Size size)
    {
        Size screen = GetScreenBounds().Size;
        int screenWidth = screen.Width;
        int screenHeight = screen.Height;
        List<Window> existingWindows = GetAllWindows();

        int bestX = 50;
        int bestY = 50;
        int minOverlap = int.MaxValue;
        int maxSpaciousness = -1;

        const int step = 40;
        int maxX = Math.Max(0, screenWidth - size.Width);
        int maxY = Math.Max(0, screenHeight - size.Height);

        for (int y = 0; y <= maxY; y += step)
        {
            for (int x = 0; x <= maxX; x += step)
            {
                int currentOverlap = 0;
                int minWindowDist = int.MaxValue;

                // Edges of the proposed window
                int rectLeft = x;
                int rectTop = y;
                int rectRight = x + size.Width;
                int rectBottom = y + size.Height;

                foreach (Window win in existingWindows)
                {
                    int winLeft = win.Position.X;
                    int winTop = win.Position.Y;
                    int winRight = win.Position.X + win.Size.Width;
                    int winBottom = win.Position.Y + win.Size.Height;

                    // Calculate Overlap (AABB)
                    int intersectLeft = Math.Max(rectLeft, winLeft);
                    int intersectTop = Math.Max(rectTop, winTop);
                    int intersectRight = Math.Min(rectRight, winRight);
                    int intersectBottom = Math.Min(rectBottom, winBottom);

                    if (intersectRight > intersectLeft && intersectBottom > intersectTop)
                    {
                        currentOverlap += (intersectRight - intersectLeft) * (intersectBottom - intersectTop);
                    }

                    // Calculate True Edge-to-Edge Distance (Manhattan)
                    // If the windows overlap, dx and dy will be 0.
                    int dx = Math.Max(0, Math.Max(winLeft - rectRight, rectLeft - winRight));
                    int dy = Math.Max(0, Math.Max(winTop - rectBottom, rectTop - winBottom));

                    int dist = dx + dy;

                    if (dist < minWindowDist)
                    {
                        minWindowDist = dist;
                    }
                }

                // Calculate Distance to Screen Borders
                int borderDistLeft = x;
                int borderDistTop = y;
                int borderDistRight = screenWidth - rectRight;
                int borderDistBottom = screenHeight - rectBottom;

                int minBorderDist = Math.Min(Math.Min(borderDistLeft, borderDistTop), Math.Min(borderDistRight, borderDistBottom));

                // Spaciousness evaluates the closest boundary (either a screen edge or another window edge)
                int currentSpaciousness = existingWindows.Count == 0
                    ? minBorderDist
                    : Math.Min(minBorderDist, minWindowDist);

                // Score Evaluation
                if (currentOverlap < minOverlap)
                {
                    minOverlap = currentOverlap;
                    maxSpaciousness = currentSpaciousness;
                    bestX = x;
                    bestY = y;
                }
                else if (currentOverlap == minOverlap)
                {
                    // If overlap is tied (e.g., both are 0), pick the most spacious position
                    if (currentSpaciousness > maxSpaciousness)
                    {
                        maxSpaciousness = currentSpaciousness;
                        bestX = x;
                        bestY = y;
                    }
                }
            }
        }

        return CreateWindow(process, title, size, new Point(bestX, bestY));
    }

    /// <summary>
    /// Closes a specific window and notifies the renderer.
    /// </summary>
    public static void CloseWindow(Window window)
    {
        _ = windows.Remove(window);

        bool wasFocused = focusedWindow == window;
        if (wasFocused)
        {
            focusedWindow = null;
        }
        if (activeInteractWindow == window)
        {
            activeInteractWindow = null;
        }

        renderSource.Render([DestroyCommand(window)]);

        if (wasFocused)
        {
            FocusNextWindow(window);
        }
    }

    /// <summary>
    /// Handles a click on a window's close button: lets the window's
    /// <see cref="Window.OnCloseRequested"/> handler decide, or else closes the window and stops
    /// the owning process once it has no windows left.
    /// </summary>
    public static void RequestCloseWindow(Window window)
    {
        if (window.RaiseCloseRequested())
        {
            return;
        }

        CloseWindow(window);

        bool hasWindowsLeft;
        hasWindowsLeft = windows.Any(w => w.ProcessId == window.ProcessId);

        if (!hasWindowsLeft)
        {
            ProcessManager.StopProcess(window.ProcessId);
        }
    }

    /// <summary> Hides a window until it is focused again (e.g. from the taskbar). </summary>
    public static void MinimizeWindow(Window window)
    {
        if (window.IsMinimized)
        {
            return;
        }

        window.IsMinimized = true;
        if (activeInteractWindow == window)
        {
            activeInteractWindow = null;
        }

        renderSource.Render([DestroyCommand(window)]);

        if (focusedWindow == window)
        {
            focusedWindow = null;
            FocusNextWindow(window);
        }
    }

    /// <summary> Maximizes a window to <see cref="WorkArea"/>, or restores a maximized one. </summary>
    public static void ToggleMaximizeWindow(Window window)
    {
        Rectangle area = WorkArea ?? GetScreenBounds();
        window.SetMaximized(!window.IsMaximized, area);
        FocusWindow(window);
    }

    /// <summary>The whole screen, from the origin.</summary>
    public static Rectangle GetScreenBounds()
    {
        Canvas canvas = Canvas.GetFullScreen();
        return new Rectangle(0, 0, (int)canvas.Mode.Width, (int)canvas.Mode.Height);
    }

    /// <summary>Tells the render sources to take a window off the screen (it closed or was minimized).</summary>
    private static RenderCommand DestroyCommand(Window window)
    {
        return new RenderCommand { WindowId = window.Id, ElementId = window.Id, Type = RenderCommandType.DestroyWindow, Position = window.Position };
    }

    /// <summary>
    /// Focuses the front-most normal window other than <paramref name="except"/>, or nothing.
    /// </summary>
    private static void FocusNextWindow(Window? except)
    {
        Window? next;
        next = windows
            .Where(w => w != except && w.IsVisible && !w.IsMinimized && w.Layer == WindowLayer.Normal)
            .OrderByDescending(w => w.ZIndex)
            .FirstOrDefault();

        FocusWindow(next);
    }

    /// <summary>Returns the windows of one process, in the order they were created.</summary>
    public static List<Window> GetWindowsForProcess(Process process)
    {
        return [.. windows.Where(w => w.ProcessId == process.Id)];
    }

    /// <summary>Returns every window, in the order they were created.</summary>
    public static List<Window> GetAllWindows()
    {
        return [.. windows];
    }

    /// <summary>
    /// Closes all windows belonging to the given process ID.
    /// </summary>
    public static void CloseWindowsForProcess(int processId)
    {
        List<Window> windowsToClose = [];
        windowsToClose.AddRange(windows.Where(w => w.ProcessId == processId));
        _ = windows.RemoveAll(w => w.ProcessId == processId);

        if (windowsToClose.Count > 0)
        {
            bool hadFocus = focusedWindow != null && windowsToClose.Contains(focusedWindow);
            if (hadFocus)
            {
                focusedWindow = null;
            }
            if (activeInteractWindow != null && windowsToClose.Contains(activeInteractWindow))
            {
                activeInteractWindow = null;
            }

            renderSource.Render([.. windowsToClose.Select(DestroyCommand)]);

            if (hadFocus)
            {
                FocusNextWindow(null);
            }
        }
    }

    /// <summary>
    /// Sets the focus to the specified window, bringing it to the foreground and restoring it if
    /// it is minimized. The window stays in its <see cref="Window.Layer"/>.
    /// </summary>
    public static void FocusWindow(Window? window)
    {
        if (window is { IsMinimized: true })
        {
            window.IsMinimized = false;
            window.Invalidate();
        }

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
    /// Forces a full redraw of all windows in the system.
    /// </summary>
    public static void InvalidateAll()
    {
        foreach (Window window in GetAllWindows())
        {
            window.Invalidate();
        }

        sentCursorPosition = null;
    }

    private static Window? TryBeginInteract(Point pointerPosition)
    {
        foreach (Window window in GetAllWindows().OrderByDescending(w => w.Layer).ThenByDescending(w => w.ZIndex))
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

    private sealed class MultiRenderSource(List<IRenderSource> sources) : IRenderSource
    {

        public void AddSource(IRenderSource source)
        {
            sources.Add(source);
        }

        public void RemoveSource(IRenderSource source)
        {
            _ = sources.Remove(source);
        }

        public void Render(IEnumerable<RenderCommand> commands)
        {
            List<IRenderSource> sourcesCopy;
            sourcesCopy = sources.ToList();

            foreach (IRenderSource source in sourcesCopy)
            {
                source.Render(commands);
            }
        }

        public void Composite()
        {
            List<IRenderSource> sourcesCopy;
            sourcesCopy = sources.ToList();

            foreach (IRenderSource source in sourcesCopy)
            {
                source.Composite();
            }
        }
    }
}
