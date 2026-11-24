using RemSox.Kernel.Accounts;
using RemSox.Kernel.Configuration;
using RemSox.Kernel.Processing;
using RemSox.Kernel.Programs;
using RemSox.Kernel.UI.GUI;
using RemSox.Kernel.UI.GUI.Layout;
using RemSox.Kernel.UI.GUI.UIEelements.Controls;
using RemSox.Kernel.UI.GUI.Windows;
using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

using Shapes = RemSox.Kernel.UI.GUI.UIEelements.Shapes;
using Text = RemSox.Kernel.UI.GUI.UIEelements.Shapes.Text;

namespace RemSox.Kernel.Processes;

/// <summary>
/// The desktop: taskbar and start menu on the screen of the session that started it, one per
/// session. "Log out" in the start menu ends that session, which stops the desktop and
/// everything started from it; so does stopping the desktop of a session at the machine or by
/// remote desktop, which would otherwise stay logged in with nothing to show its windows.
/// </summary>
internal sealed class DesktopProcess() : Process("Desktop Manager")
{
    private Window taskbar = null!;
    private Window? startMenu;
    private Button startButton = null!;

    private readonly List<(int WindowId, Button Button)> windowButtons = [];
    private int tickCount;

    /// <summary>
    /// Returns <paramref name="account"/>'s desktop session, the one session it works in
    /// graphically, or null when no desktop of it runs. Logging in at the machine or by remote
    /// desktop resumes it rather than starting another.
    /// </summary>
    internal static LogonSession? FindSession(Account account)
    {
        return ProcessManager.GetProcessesOfType<DesktopProcess>()
            .Where(desktop => desktop.IsRunning && desktop.Session is { RunsStartup: true, HasEnded: false } && desktop.Session.Account.Id == account.Id)
            .Select(desktop => desktop.Session)
            .FirstOrDefault();
    }

    internal override void Start(string[] args)
    {
        CreateTaskbar();
    }

    internal override void Stop()
    {
        Screen.WorkArea = null;

        // A Telnet user who started a desktop keeps their Telnet login.
        if (Session.RunsStartup)
        {
            LogonSessions.End(Session);
        }
    }

    private const int TaskbarHeight = 40;
    private const int BarPadding = 4;
    private const int BarFontSize = 16;
    private const int TaskButtonWidth = 160;

    // The taskbar's clock, and the minute it shows (so it is redrawn only when that changes).
    private Text clock = null!;
    private int clockMinute = -1;

    // The line under the focused window's taskbar button.
    private Shapes.Rectangle activeMark = null!;

    private void CreateTaskbar()
    {
        Rectangle screen = Screen.GetScreenBounds();
        int screenW = screen.Width;
        int screenH = screen.Height;
        const int taskbarH = TaskbarHeight;

        Screen.WorkArea = new Rectangle(0, 0, screenW, screenH - taskbarH);

        taskbar = Screen.CreateWindow(this, "Taskbar",
            new Size(screenW, taskbarH),
            new Point(0, screenH - taskbarH),
            activate: false);
        taskbar.HasChrome = false;
        taskbar.IsRounded = false;
        taskbar.IsResizable = false;
        taskbar.IsDraggable = false;
        taskbar.Layer = WindowLayer.Foreground;
        taskbar.AutoFlush = true;

        // A window without a frame has no background of its own: the panel is the bar's, with
        // a hairline along its top.
        _ = taskbar.CreateUIElement<Panel>(p => p.Size = new Size(screenW, taskbarH));
        _ = taskbar.CreateUIElement<Shapes.Rectangle>(r =>
        {
            r.Size = new Size(screenW, 1);
            r.Color = ThemeRole.Border;
        });

        startButton = taskbar.CreateUIElement<Button>(b =>
        {
            b.Position = new Point(BarPadding, BarPadding);
            b.Size = new Size(72, taskbarH - (2 * BarPadding));
            b.Text = "Start";
            b.FontSize = BarFontSize;
            b.BackgroundColor = ThemeRole.Accent;
            b.TextColor = ThemeRole.TextOnAccent;
        });

        startButton.OnClick += (_, _) => ToggleStartMenu();

        activeMark = taskbar.CreateUIElement<Shapes.Rectangle>(r =>
        {
            r.Color = ThemeRole.Accent;
            r.CornerRadius = 1;
            r.ZIndex = 1;
        });

        clock = taskbar.CreateUIElement<Text>(t =>
        {
            t.FontSize = BarFontSize;
            t.Position = new Point(screenW - 12 - (5 * Utils.CanvasExtensions.GetCellWidth(BarFontSize)), (taskbarH - BarFontSize) / 2);
        });
        UpdateClock();
    }

    /// <summary>Shows the time, hours and minutes, when the minute changed.</summary>
    private void UpdateClock()
    {
        DateTime now = DateTime.Now;
        if (now.Minute != clockMinute)
        {
            clockMinute = now.Minute;
            clock.Content = now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private const int MenuWidth = 240;
    private const int MenuPadding = 8;
    private const int MenuRowHeight = 30;
    private const int MenuRowGap = 2;
    private const int MenuNameHeight = 24;

    private void ToggleStartMenu()
    {
        if (startMenu is not null)
        {
            Screen.CloseWindow(startMenu);
            startMenu = null;
            return;
        }

        int screenH = Screen.GetScreenBounds().Height;

        // Read on every opening, so a shortcut added or edited shows up at once.
        List<StartMenuEntry> shortcuts = Shortcut.ReadStartMenu(Caller);
        const int sessionRows = 4;
        int programRows = shortcuts.Count + 1;
        int separator = (2 * MenuPadding) + 1;
        int menuH = MenuPadding + (programRows * (MenuRowHeight + MenuRowGap)) - MenuRowGap
            + separator + MenuNameHeight + (sessionRows * (MenuRowHeight + MenuRowGap)) - MenuRowGap + MenuPadding;

        startMenu = Screen.CreateWindow(this, "Start Menu",
            new Size(MenuWidth, menuH),
            new Point(BarPadding, screenH - TaskbarHeight - BarPadding - menuH),
            activate: false);
        startMenu.HasChrome = false;
        startMenu.IsResizable = false;
        startMenu.IsDraggable = false;
        startMenu.Layer = WindowLayer.Foreground;
        startMenu.AutoFlush = true;

        // A rounded panel: the window's corners are rounded when composited, the outline here.
        _ = startMenu.CreateUIElement<Panel>(p => p.Size = new Size(MenuWidth, menuH));
        _ = startMenu.CreateUIElement<Shapes.Rectangle>(r =>
        {
            r.Size = new Size(MenuWidth, menuH);
            r.IsFilled = false;
            r.Color = ThemeRole.Border;
            r.CornerRadius = startMenu.CornerRadius;
        });

        int y = MenuPadding;

        // Each entry is a row: the panel's colour at rest, lit under the pointer. Its action
        // returns why it could not be done, or null.
        void AddItem(string title, Func<string?> run, ThemeColor? textColor = null)
        {
            int top = y;
            Button button = startMenu.CreateUIElement<Button>(b =>
            {
                b.Position = new Point(MenuPadding, top);
                b.Size = new Size(MenuWidth - (2 * MenuPadding), MenuRowHeight);
                b.Text = title;
                b.FontSize = BarFontSize;
                b.CentersText = false;
                b.BackgroundColor = ThemeRole.Surface;
                b.TextColor = textColor ?? ThemeRole.Text;
            });
            y += MenuRowHeight + MenuRowGap;

            button.OnClick += (_, _) =>
            {
                string? error = run();

                Screen.CloseWindow(startMenu);
                startMenu = null;

                if (error is not null)
                {
                    Logger.Log($"{title} failed: {error}", Logging.LogSeverity.Error);
                    _ = MessageBox.Show(this, title, error);
                }
            };
        }

        foreach (StartMenuEntry entry in shortcuts)
        {
            if (entry.Shortcut is { } shortcut)
            {
                // Without a folder of its own, it starts in the desktop's, as from a shell.
                AddItem(entry.Title, () => shortcut.Start(WorkingDirectory, Caller).Error);
            }
            else
            {
                // Greyed out; a click says what is wrong with the file, so it can be fixed.
                AddItem(entry.Title, () => $"{entry.Path} is no valid shortcut: {entry.Error}", ThemeRole.TextDisabled);
            }
        }

        // Always there, whatever the shortcut files say: with every Terminal closed and no
        // Terminal shortcut, nothing else on the desktop could open a shell.
        AddItem("Terminal", () => Launcher.Start(BuiltInPrograms.TerminalName, [], WorkingDirectory, Caller).Error);

        // A separator, then the session: whose it is, and what can be done with it.
        y += MenuPadding - MenuRowGap;
        int line = y;
        _ = startMenu.CreateUIElement<Shapes.Rectangle>(r =>
        {
            r.Position = new Point(MenuPadding, line);
            r.Size = new Size(MenuWidth - (2 * MenuPadding), 1);
            r.Color = ThemeRole.Border;
        });
        y += 1 + MenuPadding;

        int nameTop = y;
        _ = startMenu.CreateUIElement<Text>(t =>
        {
            t.Position = new Point(MenuPadding + 10, nameTop + 2);
            t.Content = Session.Account.Name;
            t.FontSize = BarFontSize;
            t.Color = ThemeRole.TextMuted;
        });
        y += MenuNameHeight;

        AddItem("Lock", () =>
        {
            LogonSessions.Lock(Session);
            return null;
        });

        AddItem("Log out", () =>
        {
            LogonSessions.End(Session);
            return null;
        });

        // From remote desktop this needs admin rights, which the start menu cannot ask for:
        // the message says to use the command with admin in a Terminal.
        AddItem("Restart", () =>
        {
            string? denied = MachinePower.Deny(Caller, "restart");
            if (denied is null)
            {
                MachinePower.Reboot();
            }

            return denied;
        }, ThemeRole.Danger);

        AddItem("Shut down", () =>
        {
            string? denied = MachinePower.Deny(Caller, "shut down");
            if (denied is null)
            {
                MachinePower.Shutdown();
            }

            return denied;
        }, ThemeRole.Danger);
    }

    internal override void Tick()
    {
        if (tickCount % 5 == 0)
        {
            UpdateWindowButtons();
            UpdateClock();
        }

        if (tickCount % IdleCheckTicks == 0)
        {
            LockIfIdle();
        }

        tickCount++;
    }

    /// <summary>How often, in ticks, the desktop checks whether its session went idle: often enough, without reading the setting every tick.</summary>
    private const int IdleCheckTicks = 50;

    /// <summary>
    /// Locks the session once nobody has used it for its account's <c>lock.idle</c>, like
    /// Windows' lock after inactivity. Only while something shows it: a locked session stays as
    /// it is until its user logs in again.
    /// </summary>
    private void LockIfIdle()
    {
        if (Session.Place is not (SessionPlace.Machine or SessionPlace.RemoteDesktop)
            || SystemSettings.GetLockIdleTime(Session.Account) is not { } limit
            || Session.IdleTime < limit)
        {
            return;
        }

        Logger.Log($"Locking session {Session.Id} of {Session.Account.Name}: no input for {limit.TotalMinutes:0} minutes.", Logging.LogSeverity.Info);
        LogonSessions.Lock(Session);
    }

    private void UpdateWindowButtons()
    {
        List<Window> allWindows = Screen.GetAllWindows()
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
        int btnX = startButton.Position.X + startButton.Size.Width + BarPadding;
        int buttonHeight = TaskbarHeight - (2 * BarPadding);
        Button? focused = null;
        windowButtons.Clear();
        foreach (Window win in allWindows)
        {
            if (tracked.TryGetValue(win.Id, out Button? existing))
            {
                // Reposition existing button
                existing.Position = new Point(btnX, BarPadding);
            }
            else
            {
                // Create new button
                existing = taskbar.CreateUIElement<Button>(b =>
                {
                    b.Position = new Point(btnX, BarPadding);
                    b.Size = new Size(TaskButtonWidth, buttonHeight);
                    b.FontSize = BarFontSize;
                    b.CentersText = false;
                    b.Text = TruncateTitle(win.Title, 17);
                });

                int capturedId = win.Id;
                existing.OnClick += (_, _) =>
                {
                    Window? target = Screen.GetAllWindows().FirstOrDefault(w => w.Id == capturedId);
                    if (target is null)
                    {
                        return;
                    }

                    // Clicking the focused window's button minimizes it; any other brings it up.
                    if (target.IsFocused && !target.IsMinimized)
                    {
                        Screen.MinimizeWindow(target);
                    }
                    else
                    {
                        Screen.FocusWindow(target);
                    }
                };
            }

            // The focused window's button stands out and gets the accent line under it; a
            // minimized window's text is dimmed.
            existing.BackgroundColor = win.IsFocused ? ThemeRole.Control : ThemeRole.Surface;
            existing.TextColor = win.IsMinimized ? ThemeRole.TextMuted : ThemeRole.Text;
            if (win.IsFocused && !win.IsMinimized)
            {
                focused = existing;
            }

            windowButtons.Add((win.Id, existing));
            btnX += TaskButtonWidth + BarPadding;
        }

        const int markWidth = 24;
        activeMark.Position = focused is null ? Point.Empty : new Point(focused.Position.X + ((TaskButtonWidth - markWidth) / 2), TaskbarHeight - BarPadding - 3);
        activeMark.Size = focused is null ? Size.Empty : new Size(markWidth, 3);
    }

    private static string TruncateTitle(string title, int maxLen)
    {
        return title.Length <= maxLen ? title : title[..(maxLen - 1)] + "\u2026";
    }
}
