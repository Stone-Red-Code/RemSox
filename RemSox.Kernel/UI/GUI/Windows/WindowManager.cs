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
    private static readonly Lock windowsLock = new();
    // Process ID to list of windows
    private static readonly Dictionary<int, List<Window>> windows = [];

    private static int nextWindowId = 1;
    private static int nextZIndex = 1;

    private static Window? focusedWindow;

    private static Window? activeInteractWindow = null;
    private static Point lastPointerPosition = Point.Empty;

    // Last cursor position sent to the render sources; null forces a resend (e.g. after a remote sync).
    private static Point? sentCursorPosition;
    private static bool wasLeftButtonDown = false;
    private static bool wasRightButtonDown = false;
    private static bool wasMiddleButtonDown = false;

    private static readonly MuliRenderSource renderSource = new([]);

    private static readonly Queue<MouseEvent> mouseQueue = [];
    private static readonly Queue<KeyEvent> keyQueue = [];
    private static readonly Lock inputLock = new();

    /// <summary> Enqueues a mouse event from any input source. </summary>
    public static void EnqueueMouseEvent(MouseEvent mouseEvent)
    {
        lock (inputLock)
        {
            mouseQueue.Enqueue(mouseEvent);
        }
    }

    /// <summary> Enqueues a keyboard event from any input source. </summary>
    public static void EnqueueKeyEvent(KeyEvent keyEvent)
    {
        lock (inputLock)
        {
            keyQueue.Enqueue(keyEvent);
        }
    }
    /// <summary>
    /// Processes queued input and updates interaction state,
    /// and triggers rendering.
    /// </summary>
    internal static void Update()
    {
        InputState input = DrainInput();

        Point pointerPosition = input.Position;
        bool leftButtonDown = input.LeftButton;
        bool rightButtonDown = input.RightButton;
        bool middleButtonDown = input.MiddleButton;

        Canvas canvas = Canvas.GetFullScreen();

        // Mouse events go to the window a press landed on until the button is released (so a
        // foreground window such as the taskbar gets its clicks without taking keyboard focus), and
        // to the focused window otherwise.
        Window? mouseTarget = focusedWindow;
        Window? chromeClickWindow = null;
        Window.ChromeButton chromeClick = Window.ChromeButton.None;

        if (leftButtonDown && !wasLeftButtonDown)
        {
            activeInteractWindow = TryBeginInteract(pointerPosition);
            mouseTarget = activeInteractWindow ?? focusedWindow;
        }
        else if (leftButtonDown && activeInteractWindow is not null)
        {
            activeInteractWindow.UpdateInteraction(pointerPosition, new Size((int)canvas.Mode.Width, (int)canvas.Mode.Height));
            mouseTarget = activeInteractWindow;
        }
        else if (!leftButtonDown && activeInteractWindow is not null)
        {
            mouseTarget = activeInteractWindow;
            chromeClickWindow = activeInteractWindow;
            chromeClick = activeInteractWindow.EndInteraction(pointerPosition);
            activeInteractWindow = null;
        }

        foreach (KeyEvent keyEvent in input.KeyEvents)
        {
            focusedWindow?.HandleKeyEvent(keyEvent);
        }

        if (mouseTarget is not null)
        {
            DispatchMouseEvents(mouseTarget, pointerPosition,
                leftButtonDown, wasLeftButtonDown,
                rightButtonDown, wasRightButtonDown,
                middleButtonDown, wasMiddleButtonDown,
                input.ScrollDelta);
        }

        // Run title bar button actions last: closing a window can stop its process.
        if (chromeClickWindow is not null)
        {
            switch (chromeClick)
            {
                case Window.ChromeButton.Close:
                    RequestCloseWindow(chromeClickWindow);
                    break;
                case Window.ChromeButton.Maximize:
                    ToggleMaximizeWindow(chromeClickWindow);
                    break;
                case Window.ChromeButton.Minimize:
                    MinimizeWindow(chromeClickWindow);
                    break;
            }
        }

        wasLeftButtonDown = leftButtonDown;
        wasRightButtonDown = rightButtonDown;
        wasMiddleButtonDown = middleButtonDown;

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
        Canvas canvas = Canvas.GetFullScreen();
        int screenWidth = (int)canvas.Mode.Width;
        int screenHeight = (int)canvas.Mode.Height;

        List<Window> existingWindows;
        lock (windowsLock)
        {
            existingWindows = windows.Values.SelectMany(w => w).ToList();
        }

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
        lock (windowsLock)
        {
            if (windows.TryGetValue(window.ProcessId, out List<Window>? processWindows))
            {
                _ = processWindows.Remove(window);
            }
        }

        bool wasFocused = focusedWindow == window;
        if (wasFocused)
        {
            focusedWindow = null;
        }
        if (activeInteractWindow == window)
        {
            activeInteractWindow = null;
        }

        renderSource.Render([new RenderCommand { WindowId = window.Id, ElementId = window.Id, Type = RenderCommandType.DestroyWindow, Position = window.Position }]);

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
        lock (windowsLock)
        {
            hasWindowsLeft = windows.TryGetValue(window.ProcessId, out List<Window>? processWindows) && processWindows.Count > 0;
        }

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

        renderSource.Render([new RenderCommand { WindowId = window.Id, ElementId = window.Id, Type = RenderCommandType.DestroyWindow, Position = window.Position }]);

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

    private static Rectangle GetScreenBounds()
    {
        Canvas canvas = Canvas.GetFullScreen();
        return new Rectangle(0, 0, (int)canvas.Mode.Width, (int)canvas.Mode.Height);
    }

    /// <summary>
    /// Focuses the front-most normal window other than <paramref name="except"/>, or nothing.
    /// </summary>
    private static void FocusNextWindow(Window? except)
    {
        Window? next;
        lock (windowsLock)
        {
            next = windows.Values
                .SelectMany(w => w)
                .Where(w => w != except && w.IsVisible && !w.IsMinimized && w.Layer == WindowLayer.Normal)
                .OrderByDescending(w => w.ZIndex)
                .FirstOrDefault();
        }

        FocusWindow(next);
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
    /// Returns all windows across all processes, ordered by Z-index (highest first).
    /// </summary>
    public static List<Window> GetAllWindows()
    {
        lock (windowsLock)
        {
            return windows.Values
                .SelectMany(w => w)
                .OrderBy(w => w.Id)
                .ToList();
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
            bool hadFocus = focusedWindow != null && windowsToClose.Contains(focusedWindow);
            if (hadFocus)
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
                closeCommands.Add(new RenderCommand { WindowId = window.Id, ElementId = window.Id, Type = RenderCommandType.DestroyWindow, Position = window.Position });
            }
            renderSource.Render(closeCommands);

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
        List<Window> allWindows;
        lock (windowsLock)
        {
            allWindows = windows.Values.SelectMany(w => w).ToList();
        }

        foreach (Window window in allWindows)
        {
            window.Invalidate();
        }

        sentCursorPosition = null;
    }

    private static Window? TryBeginInteract(Point pointerPosition)
    {
        List<Window> allWindows;
        lock (windowsLock)
        {
            allWindows = windows.Values.SelectMany(w => w).OrderByDescending(w => w.Layer).ThenByDescending(w => w.ZIndex).ToList();
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

    private static InputState DrainInput()
    {
        List<MouseEvent> mouseEvents;
        List<KeyEvent> keyEvents;

        lock (inputLock)
        {
            mouseEvents = [.. mouseQueue];
            mouseQueue.Clear();
            keyEvents = [.. keyQueue];
            keyQueue.Clear();
        }

        Point pos = lastPointerPosition;
        bool left = wasLeftButtonDown, right = wasRightButtonDown, middle = wasMiddleButtonDown;
        int scroll = 0;

        foreach (MouseEvent e in mouseEvents)
        {
            switch (e.Type)
            {
                case MouseEventType.Move:
                    pos = new Point(e.X, e.Y);
                    break;
                case MouseEventType.ButtonDown:
                    if (e.Button == MouseButton.Left) { left = true; }
                    else if (e.Button == MouseButton.Right) { right = true; }
                    else if (e.Button == MouseButton.Middle) { middle = true; }
                    break;
                case MouseEventType.ButtonUp:
                    if (e.Button == MouseButton.Left) { left = false; }
                    else if (e.Button == MouseButton.Right) { right = false; }
                    else if (e.Button == MouseButton.Middle) { middle = false; }
                    break;
                case MouseEventType.Wheel:
                    scroll += e.Delta;
                    break;
            }
        }

        return new InputState(pos, left, right, middle, scroll, keyEvents);
    }

    private static void DispatchMouseEvents(
        Window window, Point pos,
        bool leftDown, bool wasLeftDown,
        bool rightDown, bool wasRightDown,
        bool middleDown, bool wasMiddleDown,
        int scrollDelta)
    {
        if (pos != lastPointerPosition)
        {
            window.HandleMouseEvent(MouseEvent.Move(pos.X, pos.Y));
        }

        if (leftDown && !wasLeftDown)
        {
            window.HandleMouseEvent(MouseEvent.ButtonDown(pos.X, pos.Y, MouseButton.Left));
        }
        else if (!leftDown && wasLeftDown)
        {
            window.HandleMouseEvent(MouseEvent.ButtonUp(pos.X, pos.Y, MouseButton.Left));
        }

        if (rightDown && !wasRightDown)
        {
            window.HandleMouseEvent(MouseEvent.ButtonDown(pos.X, pos.Y, MouseButton.Right));
        }
        else if (!rightDown && wasRightDown)
        {
            window.HandleMouseEvent(MouseEvent.ButtonUp(pos.X, pos.Y, MouseButton.Right));
        }

        if (middleDown && !wasMiddleDown)
        {
            window.HandleMouseEvent(MouseEvent.ButtonDown(pos.X, pos.Y, MouseButton.Middle));
        }
        else if (!middleDown && wasMiddleDown)
        {
            window.HandleMouseEvent(MouseEvent.ButtonUp(pos.X, pos.Y, MouseButton.Middle));
        }

        if (scrollDelta != 0)
        {
            window.HandleMouseEvent(MouseEvent.Wheel(pos.X, pos.Y, scrollDelta));
        }
    }

    private static int GetNextWindowId()
    {
        return nextWindowId++;
    }

    private readonly record struct InputState(
        Point Position,
        bool LeftButton,
        bool RightButton,
        bool MiddleButton,
        int ScrollDelta,
        List<KeyEvent> KeyEvents
    );

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

        public void Composite()
        {
            List<IRenderSource> sourcesCopy;
            lock (sourcesLock)
            {
                sourcesCopy = sources.ToList();
            }

            foreach (IRenderSource source in sourcesCopy)
            {
                source.Composite();
            }
        }
    }
}
