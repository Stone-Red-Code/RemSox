using Cosmos.Kernel.System.Input;

using RemSox.Kernel.Processing;
using RemSox.Kernel.Security;
using RemSox.Kernel.UI.GUI;
using RemSox.Kernel.UI.GUI.UIEelements.Controls;
using RemSox.Kernel.UI.GUI.UIEelements.Shapes;
using RemSox.Kernel.UI.GUI.Windows;

using System.Drawing;

namespace RemSox.Kernel.Processes;

/// <summary>
/// The GUI text editor (<c>textedit</c>): a window with the file's path, a Save button, the text
/// (a <see cref="TextArea"/>) and a status line. Ctrl+S saves too. A file that does not exist
/// yet is created by the first save; the path can be changed before saving, to save elsewhere.
/// Closing the window with unsaved changes asks for a second click on its close button.
/// </summary>
/// <remarks>
/// Files are read and written through <see cref="UserFiles"/> as the user who started it, so
/// the permission rules apply as for <c>edit</c>; tabs become spaces when a file is read.
/// </remarks>
public sealed class TextEditProcess() : Process("Text Editor")
{
    private const int Margin = 4;
    private const int RowHeight = 22;
    private const int ToolbarTop = Window.TitleBarHeight + Margin;
    private const int AreaTop = ToolbarTop + RowHeight + Margin;
    private const int SaveWidth = 60;
    private const int ScrollBarWidth = 10;
    private const int StatusFontSize = 16;

    private static readonly ThemeColor StatusColor = ThemeRole.TextMuted;
    private static readonly ThemeColor ErrorColor = ThemeRole.Danger;

    private Window window = null!;
    private TextBox pathBox = null!;
    private Button saveButton = null!;
    private TextArea area = null!;
    private ScrollBar scrollBar = null!;
    private Text statusText = null!;

    private bool modified;
    private bool warnedOnClose;
    private Size lastSize = new(-1, -1);

    /// <param name="args">The file to edit, as a full path (the <c>textedit</c> program's <c>Prepare</c> makes it one); none for a new file.</param>
    internal override void Start(string[] args)
    {
        window = CreateWindow("Text Editor", new Size(560, 400));
        window.AutoFlush = true;

        pathBox = window.CreateUIElement<TextBox>(b =>
        {
            b.Position = new Point(Margin, ToolbarTop);
        });
        pathBox.Submitted += _ => Save();

        saveButton = window.CreateUIElement<Button>(b => b.Text = "Save");
        saveButton.OnClick += (_, _) => Save();

        area = window.CreateUIElement<TextArea>(a =>
        {
            a.Position = new Point(Margin, AreaTop);
        });
        area.TextChanged += () =>
        {
            if (!modified)
            {
                modified = true;
                UpdateTitle();
            }
        };

        scrollBar = window.CreateUIElement<ScrollBar>();
        area.ScrollBar = scrollBar;

        statusText = window.CreateUIElement<Text>(t =>
        {
            t.Color = StatusColor;
            t.FontSize = StatusFontSize;
        });

        window.OnKeyEvent += HandleKey;
        window.OnCloseRequested += OnCloseRequested;

        Layout();
        if (args.Length > 0)
        {
            Load(args[0]);
        }
        else
        {
            pathBox.Text = Path.Combine(WorkingDirectory, "untitled.txt").Replace('\\', '/');
            SetStatus("New file: choose its path above, then save.", isError: false);
        }

        UpdateTitle();
        window.Focus(area);
    }

    internal override void Tick()
    {
        if (window.Size != lastSize && !window.IsResizing)
        {
            Layout();
        }
    }

    internal override void Stop()
    {
        window.OnKeyEvent -= HandleKey;
    }

    private void Layout()
    {
        lastSize = window.Size;
        int width = window.Size.Width;
        int height = window.Size.Height;

        pathBox.Size = new Size(Math.Max(40, width - (3 * Margin) - SaveWidth), RowHeight);
        saveButton.Position = new Point(width - Margin - SaveWidth, ToolbarTop);
        saveButton.Size = new Size(SaveWidth, RowHeight);

        int areaHeight = Math.Max(RowHeight, height - AreaTop - RowHeight - (2 * Margin));
        area.Size = new Size(Math.Max(40, width - (3 * Margin) - ScrollBarWidth), areaHeight);
        scrollBar.Position = new Point(width - Margin - ScrollBarWidth, AreaTop);
        scrollBar.Size = new Size(ScrollBarWidth, areaHeight);
        area.ScrollTo(area.TopLine);
        area.ScrollBar = scrollBar;

        statusText.Position = new Point(Margin, AreaTop + areaHeight + Margin);
        statusText.MaxWidth = Math.Max(0, width - (2 * Margin));
    }

    private void Load(string path)
    {
        pathBox.Text = path;
        if (!UserFiles.Exists(Caller, path))
        {
            SetStatus("New file: it is created when you save.", isError: false);
            return;
        }

        try
        {
            area.SetLines(UserFiles.ReadAllLines(Caller, path));
            SetStatus($"Read {area.Lines.Count} line(s).", isError: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus("Cannot read the file: " + ex.Message, isError: true);
        }
    }

    private void Save()
    {
        string path = pathBox.Text.Trim();
        if (path.Length == 0)
        {
            SetStatus("Give the file a path first.", isError: true);
            return;
        }

        try
        {
            path = Path.GetFullPath(path, WorkingDirectory).Replace('\\', '/');
            UserFiles.SafeWriteAllLines(Caller, path, area.Lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetStatus("Not saved: " + ex.Message, isError: true);
            return;
        }

        pathBox.Text = path;
        modified = false;
        warnedOnClose = false;
        UpdateTitle();
        SetStatus($"Saved {area.Lines.Count} line(s) to {path}.", isError: false);
        window.Focus(area);
    }

    private void HandleKey(KeyEvent keyEvent)
    {
        if (keyEvent.Key == Key.S && keyEvent.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            Save();
        }
    }

    private void OnCloseRequested(Window closing)
    {
        if (modified && !warnedOnClose)
        {
            warnedOnClose = true;
            SetStatus("Unsaved changes: save, or close again to throw them away.", isError: true);
            return;
        }

        closing.Screen.CloseWindow(closing);
        ProcessManager.StopProcess(Id);
    }

    private void UpdateTitle()
    {
        string name = Path.GetFileName(pathBox.Text.TrimEnd('/'));
        window.Title = (modified ? "* " : string.Empty) + (name.Length > 0 ? name : "untitled") + " - Text Editor";
        window.Flush();
    }

    private void SetStatus(string message, bool isError)
    {
        statusText.Content = message;
        statusText.Color = isError ? ErrorColor : StatusColor;
    }
}
