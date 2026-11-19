using RemSox.Kernel.UI.GUI.Rendering;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;

using RemSox.Kernel.Processing;
using RemSox.Shared.UI;
using RemSox.Kernel.UI.GUI.Layout;
using RemSox.Shared.UI.GUI.Rendering;
using RemSox.Kernel.UI.GUI.UIEelements.Controls;
using RemSox.Kernel.UI.GUI.Windows;

using System.Drawing;

namespace RemSox.Kernel.Processes;

internal class DesktopProcess() : Process("Desktop Manager")
{
    private static bool isGraphicsInitialized = false;

    // Each app gets the desktop's working directory, like a program started from a shell.
    private static readonly (string Name, Func<string, int> Spawn)[] AvailableApps = [
        ("Terminal",     folder => ProcessManager.SpawnProcess<TerminalProcess>(workingDirectory: folder)),
        ("Test Process", folder => ProcessManager.SpawnProcess<TestProcess>(workingDirectory: folder)),
    ];

    private Window taskbar = null!;
    private Window? startMenu;
    private Button startButton = null!;

    private readonly List<(int WindowId, Button Button)> windowButtons = [];
    private int tickCount;

    internal override void Start(string[] args)
    {
        if (!isGraphicsInitialized)
        {
            WindowManager.AddRenderSource(new CanvasRenderSource());
            isGraphicsInitialized = true;
        }

        WindowManager.InvalidateAll();
        CreateTaskbar();

        // Open one Terminal to start with; after that, Terminals are opened from the start menu.
        if (!ProcessManager.IsProcessRunning<TerminalProcess>())
        {
            _ = ProcessManager.SpawnProcess<TerminalProcess>(workingDirectory: WorkingDirectory);
        }
    }

    internal override void Stop()
    {
        WindowManager.WorkArea = null;
    }

    private const int TaskbarHeight = 36;

    private void CreateTaskbar()
    {
        Canvas canvas = Canvas.GetFullScreen();
        int screenW = (int)canvas.Mode.Width;
        int screenH = (int)canvas.Mode.Height;
        const int taskbarH = TaskbarHeight;

        WindowManager.WorkArea = new Rectangle(0, 0, screenW, screenH - taskbarH);

        taskbar = WindowManager.CreateWindow(this, "Taskbar",
            new Size(screenW, taskbarH),
            new Point(0, screenH - taskbarH),
            activate: false);
        taskbar.HasChrome = false;
        taskbar.IsResizable = false;
        taskbar.IsDraggable = false;
        taskbar.Layer = WindowLayer.Foreground;
        taskbar.AutoFlush = true;

        startButton = taskbar.CreateUIElement<Button>(b =>
        {
            b.Position = new Point(2, 2);
            b.Size = new Size(65, taskbarH - 4);
            b.Text = "Start";
            b.BackgroundColor = Color.FromArgb(0, 100, 180);
            b.TextColor = Color.White;
        });

        startButton.OnClick += (_, _) => ToggleStartMenu();
    }

    private void ToggleStartMenu()
    {
        if (startMenu is not null)
        {
            WindowManager.CloseWindow(startMenu);
            startMenu = null;
            return;
        }

        Canvas canvas = Canvas.GetFullScreen();
        int screenH = (int)canvas.Mode.Height;
        int menuW = 150;
        int itemH = 22;
        int menuH = (AvailableApps.Length * itemH) + 10;
        int menuX = 2;
        int menuY = screenH - TaskbarHeight - menuH;

        startMenu = WindowManager.CreateWindow(this, "Start Menu",
            new Size(menuW, menuH),
            new Point(menuX, menuY),
            activate: false);
        startMenu.HasChrome = false;
        startMenu.IsResizable = false;
        startMenu.IsDraggable = false;
        startMenu.Layer = WindowLayer.Foreground;
        startMenu.AutoFlush = true;

        StackLayout stack = startMenu.CreateStackLayout(5, 5, 2);
        stack.UniformWidth = menuW - 10;

        foreach ((string? name, Func<string, int>? spawn) in AvailableApps)
        {
            Button appBtn = stack.Add<Button>(b =>
            {
                b.Size = new Size(menuW - 10, itemH);
                b.Text = name;
                b.BackgroundColor = Color.FromArgb(60, 60, 65);
                b.TextColor = Color.White;
            });

            appBtn.OnClick += (_, _) =>
            {
                _ = spawn(WorkingDirectory);
                WindowManager.CloseWindow(startMenu);
                startMenu = null;
            };
        }
    }

    private Point lastLocalMousePos = Point.Empty;
    private bool lastLocalLeft, lastLocalRight, lastLocalMiddle;

    internal override void Tick()
    {
        PollLocalHardware();

        WindowManager.Update();

        if (tickCount % 5 == 0)
        {
            UpdateWindowButtons();
        }

        tickCount++;
    }

    private void PollLocalHardware()
    {
        Point pos = new(MouseManager.X, MouseManager.Y);

        if (pos != lastLocalMousePos)
        {
            WindowManager.EnqueueMouseEvent(MouseEvent.Move(pos.X, pos.Y));
            lastLocalMousePos = pos;
        }

        bool left = MouseManager.LeftButton;
        if (left != lastLocalLeft)
        {
            WindowManager.EnqueueMouseEvent(left
                ? MouseEvent.ButtonDown(pos.X, pos.Y, MouseButton.Left)
                : MouseEvent.ButtonUp(pos.X, pos.Y, MouseButton.Left));
            lastLocalLeft = left;
        }

        bool right = MouseManager.RightButton;
        if (right != lastLocalRight)
        {
            WindowManager.EnqueueMouseEvent(right
                ? MouseEvent.ButtonDown(pos.X, pos.Y, MouseButton.Right)
                : MouseEvent.ButtonUp(pos.X, pos.Y, MouseButton.Right));
            lastLocalRight = right;
        }

        bool middle = MouseManager.MiddleButton;
        if (middle != lastLocalMiddle)
        {
            WindowManager.EnqueueMouseEvent(middle
                ? MouseEvent.ButtonDown(pos.X, pos.Y, MouseButton.Middle)
                : MouseEvent.ButtonUp(pos.X, pos.Y, MouseButton.Middle));
            lastLocalMiddle = middle;
        }

        int scroll = MouseManager.ScrollDelta;
        if (scroll != 0)
        {
            MouseManager.ResetScrollDelta();
            WindowManager.EnqueueMouseEvent(MouseEvent.Wheel(pos.X, pos.Y, scroll));
        }

        while (KeyboardManager.TryReadKey(out KeyEvent? keyEvent) && keyEvent is not null)
        {
            WindowManager.EnqueueKeyEvent(keyEvent);
        }
    }

    private void UpdateWindowButtons()
    {
        List<Window> allWindows = WindowManager.GetAllWindows()
            .Where(w => w != taskbar && w != startMenu && w.HasChrome)
            .ToList();

        // Build lookup of current window IDs
        HashSet<int> currentIds = [];
        foreach (Window w in allWindows)
        {
            _ = currentIds.Add(w.Id);
        }

        // Remove buttons for windows that no longer exist
        for (int i = windowButtons.Count - 1; i >= 0; i--)
        {
            (int winId, Button btn) = windowButtons[i];
            if (!currentIds.Contains(winId))
            {
                taskbar.RemoveUIElement(btn.Id);
                windowButtons.RemoveAt(i);
            }
        }

        // Build lookup of existing tracked windows
        Dictionary<int, Button> tracked = [];
        foreach ((int winId, Button? btn) in windowButtons)
        {
            tracked[winId] = btn;
        }

        // Add new buttons and reposition everything in one pass
        int btnX = startButton.Size.Width + 6;
        windowButtons.Clear();
        foreach (Window win in allWindows)
        {
            if (tracked.TryGetValue(win.Id, out Button? existing))
            {
                // Reposition existing button
                existing.Position = new Point(btnX, 2);
            }
            else
            {
                // Create new button
                existing = taskbar.CreateUIElement<Button>(b =>
                {
                    b.Position = new Point(btnX, 2);
                    b.Size = new Size(100, 32);
                    b.Text = TruncateTitle(win.Title, 12);
                    b.BackgroundColor = Color.FromArgb(55, 55, 60);
                    b.TextColor = Color.White;
                });

                int capturedId = win.Id;
                existing.OnClick += (_, _) =>
                {
                    Window? target = WindowManager.GetAllWindows().FirstOrDefault(w => w.Id == capturedId);
                    if (target is null)
                    {
                        return;
                    }

                    // Clicking the focused window's button minimizes it; any other brings it up.
                    if (target.IsFocused && !target.IsMinimized)
                    {
                        WindowManager.MinimizeWindow(target);
                    }
                    else
                    {
                        WindowManager.FocusWindow(target);
                    }
                };
            }

            // Highlight the focused window; dim minimized ones
            existing.BackgroundColor = win.IsFocused
                ? Color.FromArgb(0, 90, 160)
                : win.IsMinimized
                    ? Color.FromArgb(35, 35, 40)
                    : Color.FromArgb(55, 55, 60);

            windowButtons.Add((win.Id, existing));
            btnX += 104;
        }
    }

    private static string TruncateTitle(string title, int maxLen)
    {
        return title.Length <= maxLen ? title : title[..(maxLen - 1)] + "\u2026";
    }
}
