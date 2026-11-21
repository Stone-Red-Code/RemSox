using RemSox.Kernel.Accounts;
using RemSox.Kernel.UI.GUI;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;

using RemSox.Kernel.Processing;
using RemSox.Kernel.Programs;
using RemSox.Shared.UI;
using RemSox.Kernel.UI.GUI.Layout;
using RemSox.Shared.UI.GUI.Rendering;
using RemSox.Kernel.UI.GUI.UIEelements.Controls;
using RemSox.Kernel.UI.GUI.Windows;

using System.Drawing;

namespace RemSox.Kernel.Processes;

/// <summary>
/// The desktop: taskbar and start menu, in the session of whoever started it. "Log out" in the
/// start menu ends that session, which stops the desktop and everything started from it; so
/// does stopping the desktop of a session at the machine or by remote desktop, which would
/// otherwise stay logged in with its other windows still open and nothing to manage them.
/// </summary>
internal sealed class DesktopProcess() : Process("Desktop Manager")
{
    private Window taskbar = null!;
    private Window? startMenu;
    private Button startButton = null!;

    private readonly List<(int WindowId, Button Button)> windowButtons = [];
    private int tickCount;

    internal override void Start(string[] args)
    {
        LocalDisplay.Start();
        CreateTaskbar();
        UpdateLocalLock();
    }

    internal override void Stop()
    {
        WindowManager.WorkArea = null;
        LocalDisplay.Lock(null);

        // A Telnet user who started a desktop keeps their Telnet login.
        if (Session.RunsStartup)
        {
            LogonSessions.End(Session);
        }
    }

    private const int TaskbarHeight = 36;

    private void CreateTaskbar()
    {
        Rectangle screen = WindowManager.GetScreenBounds();
        int screenW = screen.Width;
        int screenH = screen.Height;
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

        int screenH = WindowManager.GetScreenBounds().Height;
        int menuW = 150;
        int itemH = 22;
        const int spacing = 2;
        // Read on every opening, so a shortcut added or edited shows up at once.
        List<Shortcut> shortcuts = Shortcut.ReadStartMenu(Session.Account);
        int items = shortcuts.Count + 2;
        int menuH = (items * (itemH + spacing)) - spacing + 10;
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

        StackLayout stack = startMenu.CreateStackLayout(5, 5, spacing);
        stack.UniformWidth = menuW - 10;

        void AddItem(string title, Color color, Func<ProgramStart> start)
        {
            Button button = stack.Add<Button>(b =>
            {
                b.Size = new Size(menuW - 10, itemH);
                b.Text = title;
                b.BackgroundColor = color;
                b.TextColor = Color.White;
            });

            button.OnClick += (_, _) =>
            {
                ProgramStart started = start();

                WindowManager.CloseWindow(startMenu);
                startMenu = null;

                if (started.Error is not null)
                {
                    Logger.Log($"Could not start {title}: {started.Error}", Logging.LogSeverity.Error);
                    _ = MessageBox.Show(this, "Could not start " + title, started.Error);
                }
            };
        }

        foreach (Shortcut shortcut in shortcuts)
        {
            // Without a folder of its own, it starts in the desktop's, as from a shell.
            AddItem(shortcut.Title, Color.FromArgb(60, 60, 65), () => shortcut.Start(WorkingDirectory, Caller));
        }

        // Always there, whatever the shortcut files say: with every Terminal closed and no
        // Terminal shortcut, nothing else on the desktop could open a shell.
        AddItem("Terminal", Color.FromArgb(0, 80, 140), () => Launcher.Start(BuiltInPrograms.TerminalName, [], WorkingDirectory, Caller));

        AddItem("Log out " + Session.Account.Name, Color.FromArgb(120, 40, 40), () =>
        {
            LogonSessions.End(Session);
            return new ProgramStart(0, null, null);
        });
    }

    internal override void Tick()
    {
        LocalDisplay.PollInput();

        WindowManager.Update();

        if (tickCount % 5 == 0)
        {
            UpdateWindowButtons();
            UpdateLocalLock();
        }

        tickCount++;
    }

    /// <summary>
    /// Hides the desktop from the local screen while it belongs to a remote session or a viewer
    /// is connected to it, so whoever sits at the machine cannot use someone's remote desktop.
    /// </summary>
    private void UpdateLocalLock()
    {
        bool remote = !Session.IsAtMachine
            || ProcessManager.GetProcessesOfType<RemoteDesktopProcess>().Any(server => server.HasViewers);

        LocalDisplay.Lock(remote ? $"This desktop is in use remotely by {Session.Account.Name}." : null);
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
