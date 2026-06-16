using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;

using RemSox.Processing;
using RemSox.UI;
using RemSox.UI.GUI.Layout;
using RemSox.UI.GUI.Rendering;
using RemSox.UI.GUI.UIEelements.Controls;
using RemSox.UI.GUI.Windows;

using System.Drawing;

namespace RemSox.Processes;

internal class DesktopProcess() : Process("Desktop Manager")
{
    private static bool isGraphicsInitialized = false;

    private static readonly (string Name, Func<int> Spawn)[] AvailableApps = [
        ("Terminal",    () => ProcessManager.SpawnProcess<TerminalProcess>()),
        ("Test Process",() => ProcessManager.SpawnProcess<TestProcess>()),
        ("YesNt",       () => ProcessManager.SpawnProcess<YesNtInterpreterProcess>()),
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
    }

    private void CreateTaskbar()
    {
        Canvas canvas = FullScreenCanvas.GetFullScreenCanvas();
        int screenW = (int)canvas.Mode.Width;
        int screenH = (int)canvas.Mode.Height;
        const int taskbarH = 36;

        taskbar = WindowManager.CreateWindow(this, "Taskbar",
            new Size(screenW, taskbarH),
            new Point(0, screenH - taskbarH));
        taskbar.HasChrome = false;
        taskbar.IsResizable = false;
        taskbar.IsDraggable = false;
        taskbar.ZIndex = int.MaxValue;
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

        Canvas canvas = FullScreenCanvas.GetFullScreenCanvas();
        int screenH = (int)canvas.Mode.Height;
        int menuW = 150;
        int itemH = 22;
        int menuH = (AvailableApps.Length * itemH) + 10;
        int menuX = 2;
        int menuY = screenH - 36 - menuH;

        startMenu = WindowManager.CreateWindow(this, "Start Menu",
            new Size(menuW, menuH),
            new Point(menuX, menuY));
        startMenu.HasChrome = false;
        startMenu.IsResizable = false;
        startMenu.IsDraggable = false;
        startMenu.ZIndex = int.MaxValue - 1;
        startMenu.AutoFlush = true;

        StackLayout stack = startMenu.CreateStackLayout(5, 5, 2);
        stack.UniformWidth = menuW - 10;

        foreach ((string? name, Func<int>? spawn) in AvailableApps)
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
                _ = spawn();
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

        if (!ProcessManager.IsProcessRunning<TerminalProcess>())
        {
            _ = ProcessManager.SpawnProcess<TerminalProcess>();
        }
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
                    if (target is not null)
                    {
                        WindowManager.FocusWindow(target);
                    }
                };
            }

            // Update highlight for focused window
            existing.BackgroundColor = win.IsFocused
                ? Color.FromArgb(0, 90, 160)
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
