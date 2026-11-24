using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Input;

using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.GUI;
using RemSox.Kernel.UI.GUI.UIEelements.Controls;
using RemSox.Kernel.UI.GUI.UIEelements.Shapes;
using RemSox.Kernel.UI.GUI.Windows;

using System.Diagnostics;
using System.Drawing;
using System.Globalization;

using Process = RemSox.Kernel.Processing.Process;

namespace RemSox.Kernel.Processes;

/// <summary>
/// The Task Manager (<c>taskmgr</c>, design: <c>docs/design/system-programs.md</c>): every
/// running process with its account, its share of the processor's time, how long its ticks
/// take and its windows, under how busy the processor is (<see cref="CpuMeter"/>) and how much memory is in use, refreshed every second, and a button that stops the selected one by the
/// same rule as <c>stop</c>. One runs per session; starting it again (Ctrl+Shift+Esc on the
/// desktop, say) brings it to the front.
/// </summary>
public sealed class TaskManagerProcess() : Process("Task Manager"), IActivatable
{
    private const int Margin = 8;
    private const int RowHeight = 26;
    private const int FontSize = 16;
    private const int ScrollBarWidth = 10;
    private const int StopButtonWidth = 120;
    private const long RefreshMilliseconds = 1000;
    private const long MessageMilliseconds = 6000;

    private static readonly IReadOnlyList<ListColumn> Columns =
    [
        new("Name", 168),
        new("ID", 56),
        new("Account", 136),
        new("CPU %", 64),
        new("Tick us", 80),
        new("Windows", 64),
    ];

    private Window window = null!;
    private ListView list = null!;
    private ScrollBar scrollBar = null!;
    private Text status = null!;
    private Button stopButton = null!;
    private Size lastSize = new(-1, -1);

    // The last refresh: when it was (a Stopwatch timestamp), and each process's and all
    // processes' tick time then, to tell how much of the time since each took.
    private long lastRefresh;
    private long lastTotalTickTime;
    private Dictionary<int, long> lastTickTimes = [];
    private readonly CpuMeter cpuMeter = new();
    private string summary = string.Empty;

    // A message replacing the summary until a time (Environment.TickCount64).
    private string? message;
    private long messageUntil;

    // The desktop the user was asked to confirm stopping, if any.
    private int? confirmingStop;

    internal override void Start(string[] args)
    {
        string title = Caller.HasAdminRights ? "Task Manager (administrator)" : "Task Manager";
        window = CreateWindow(title, new Size(640, 440));
        window.AutoFlush = true;

        scrollBar = window.CreateUIElement<ScrollBar>(_ => { });
        list = window.CreateUIElement<ListView>(l =>
        {
            l.Position = new Point(Margin, Window.TitleBarHeight + Margin);
            l.Columns = Columns;
            l.FontSize = FontSize;
        });
        list.ScrollBar = scrollBar;
        list.SortBy(0);
        list.SelectionChanged += _ => CancelConfirm();

        status = window.CreateUIElement<Text>(t =>
        {
            t.FontSize = FontSize;
            t.Color = ThemeRole.TextMuted;
        });

        stopButton = window.CreateUIElement<Button>(b =>
        {
            b.Text = "Stop process";
            b.FontSize = FontSize;
        });
        stopButton.OnClick += (_, _) => StopSelected();

        window.OnKeyEvent += HandleKey;

        Layout();
        Refresh();
        window.Focus(list);
    }

    internal override void Tick()
    {
        if (window.Size != lastSize && !window.IsResizing)
        {
            Layout();
        }

        if (ProcessMetrics.ToMicroseconds(Stopwatch.GetTimestamp() - lastRefresh) >= RefreshMilliseconds * 1000)
        {
            Refresh();
        }
        else if (message is not null && Environment.TickCount64 >= messageUntil)
        {
            ShowStatus();
        }
    }

    internal override void Stop()
    {
        window.OnKeyEvent -= HandleKey;
    }

    /// <inheritdoc/>
    public void Activate()
    {
        Screen.FocusWindow(window);
    }

    private void Layout()
    {
        lastSize = window.Size;
        int width = window.Size.Width;
        int height = window.Size.Height;
        int bottom = height - Margin - RowHeight;

        list.Size = new Size(Math.Max(40, width - (2 * Margin) - ScrollBarWidth - 2), Math.Max(RowHeight * 3, bottom - Margin - list.Position.Y));
        scrollBar.Position = new Point(list.Position.X + list.Size.Width + 2, list.Position.Y);
        scrollBar.Size = new Size(ScrollBarWidth, list.Size.Height);
        list.ScrollTo(list.TopRow);

        stopButton.Position = new Point(width - Margin - StopButtonWidth, bottom);
        stopButton.Size = new Size(StopButtonWidth, RowHeight);
        status.Position = new Point(Margin, bottom + ((RowHeight - FontSize) / 2));
        status.MaxWidth = Math.Max(0, width - (3 * Margin) - StopButtonWidth);
    }

    /// <summary>Reads every process's figures again and shows them, with the time since the last refresh.</summary>
    private void Refresh()
    {
        long now = Stopwatch.GetTimestamp();
        long elapsed = lastRefresh == 0 ? 0 : now - lastRefresh;
        long totalTickTime = ProcessManager.TotalTickTime;

        List<ListRow> rows = [];
        Dictionary<int, long> tickTimes = [];
        foreach (Process process in ProcessManager.GetAllProcesses())
        {
            if (!process.IsRunning || ProcessManager.GetMetrics(process.Id) is not { } metrics)
            {
                continue;
            }

            tickTimes[process.Id] = metrics.TotalTime;
            long since = lastTickTimes.TryGetValue(process.Id, out long before) ? metrics.TotalTime - before : 0;
            int windows = Screen.Find(process.Session)?.GetWindowsForProcess(process).Count ?? 0;

            rows.Add(new ListRow(process.Id.ToString(CultureInfo.InvariantCulture),
            [
                process.Name,
                process.Id.ToString(CultureInfo.InvariantCulture),
                process.Session.Account.Name + (process.Elevated ? " (admin)" : string.Empty),
                FormatShare(since, elapsed),
                metrics.AverageTickMicroseconds.ToString("0.0", CultureInfo.InvariantCulture),
                windows.ToString(CultureInfo.InvariantCulture),
            ]));
        }

        double cpu = cpuMeter.Measure();
        summary = elapsed > 0
            ? $"CPU {cpu.ToString("0.0", CultureInfo.InvariantCulture)}%, processes {FormatShare(totalTickTime - lastTotalTickTime, elapsed)}% ({rows.Count}), {FormatMemory()}"
            : $"{rows.Count} processes, {FormatMemory()}";

        lastRefresh = now;
        lastTotalTickTime = totalTickTime;
        lastTickTimes = tickTimes;

        list.SetRows(rows);
        ShowStatus();
    }

    // How much of the memory Cosmos's page allocator manages is in use: the heap, the
    // display's buffers and everything else, in whole megabytes.
    private static string FormatMemory()
    {
        ulong page = MemoryDiagnostics.PageSizeBytes;
        ulong total = MemoryDiagnostics.TotalPages;
        ulong used = total - Math.Min(total, MemoryDiagnostics.FreePages);
        const ulong Megabyte = 1024 * 1024;
        return $"memory {(used * page / Megabyte).ToString(CultureInfo.InvariantCulture)} of {(total * page / Megabyte).ToString(CultureInfo.InvariantCulture)} MB";
    }

    private static string FormatShare(long part, long whole)
    {
        double percent = whole > 0 ? Math.Clamp(100.0 * part / whole, 0, 100) : 0;
        return percent.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private void HandleKey(KeyEvent keyEvent)
    {
        if (keyEvent.Key == Key.Delete)
        {
            StopSelected();
        }
    }

    /// <summary>
    /// Stops the selected process, if the user may (<see cref="Process.DenyStop"/>). The desktop
    /// is stopped only on a second click, since that logs its user out.
    /// </summary>
    private void StopSelected()
    {
        if (!int.TryParse(list.SelectedKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            || ProcessManager.GetProcess(id) is not { IsRunning: true } process)
        {
            ShowMessage("Select a process to stop.");
            return;
        }

        // Shorter than the shell's message, to fit the status line.
        if (process.DenyStop(Caller) is not null)
        {
            ShowMessage(Caller.Account.IsAdmin
                ? $"{process.Name} runs as {process.Session.Account.Name}: use 'admin taskmgr' to stop it."
                : $"Only an administrator can stop {process.Name}.");
            return;
        }

        if (process is DesktopProcess && confirmingStop != id)
        {
            confirmingStop = id;
            stopButton.Text = "Really stop";
            ShowMessage(process.Session == Session
                ? "Stopping the desktop logs you out. Click Really stop to go on."
                : $"Stopping this desktop logs {process.Session.Account.Name} out. Click Really stop to go on.");
            return;
        }

        CancelConfirm();
        ProcessManager.StopProcess(id);
        ShowMessage($"Stopped {process.Name} ({id}).");
    }

    private void CancelConfirm()
    {
        if (confirmingStop is not null)
        {
            confirmingStop = null;
            stopButton.Text = "Stop process";
        }
    }

    private void ShowMessage(string text)
    {
        message = text;
        messageUntil = Environment.TickCount64 + MessageMilliseconds;
        ShowStatus();
    }

    private void ShowStatus()
    {
        if (message is not null && Environment.TickCount64 >= messageUntil)
        {
            message = null;
        }

        status.Content = message ?? summary;
    }
}
