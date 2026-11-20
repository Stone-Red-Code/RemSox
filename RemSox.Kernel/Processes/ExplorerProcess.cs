using Cosmos.Kernel.System.Input;

using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Processing;
using RemSox.Kernel.Programs;
using RemSox.Kernel.UI.CLI.Commands;
using RemSox.Kernel.UI.GUI.UIEelements.Controls;
using RemSox.Kernel.UI.GUI.UIEelements.Shapes;
using RemSox.Kernel.UI.GUI.Windows;
using RemSox.Kernel.Utils;
using RemSox.Shared.UI;

using System.Drawing;

using Shapes = RemSox.Kernel.UI.GUI.UIEelements.Shapes;

namespace RemSox.Kernel.Processes;

/// <summary>
/// A file browser window: one folder's subfolders and files, folders first. Opening a folder
/// shows it; opening a file starts it through <see cref="Launcher"/>, as the start menu would,
/// so scripts, shortcuts and associated files behave the same everywhere.
/// </summary>
/// <remarks>
/// The list is drawn like the Terminal's output: only the rows in view have elements, so a
/// folder of any size costs the same to show and nothing needs clipping.
/// </remarks>
public sealed class ExplorerProcess() : Process("Explorer")
{
    private const int Margin = 4;
    private const int ToolbarTop = Window.TitleBarHeight + Margin;
    private const int ToolbarHeight = 22;
    private const int ListTop = ToolbarTop + ToolbarHeight + Margin;
    private const int RowHeight = 22;
    private const int FontSize = 18;
    private const int StatusHeight = 22;
    private const int ScrollBarWidth = 10;
    private const int ScrollBarGap = 6;
    private const long DoubleClickMilliseconds = 500;

    private static readonly Color TextColor = Color.FromArgb(220, 220, 220);
    private static readonly Color FolderColor = Color.FromArgb(120, 180, 255);
    private static readonly Color SelectedColor = Color.FromArgb(0, 90, 160);
    private static readonly Color StatusColor = Color.FromArgb(160, 160, 160);
    private static readonly Color ErrorColor = Color.FromArgb(255, 110, 100);

    private Window window = null!;
    private Button upButton = null!;
    private Text pathText = null!;
    private Text statusText = null!;
    private ScrollBar scrollBar = null!;
    private readonly List<(Shapes.Rectangle Highlight, Text Text)> rows = [];

    private string folder = "/";
    private List<Entry> entries = [];
    private int selected;
    private int top;
    private string? message;
    private bool messageIsError;

    private Size lastSize = new(-1, -1);
    private bool displayDirty = true;

    // The last click on the list, to tell a double-click.
    private int lastClickedIndex = -1;
    private long lastClickTime;

    /// <param name="args">The folder to show, already checked by the <c>explorer</c> program's <c>Prepare</c>; none for the working directory.</param>
    internal override void Start(string[] args)
    {
        window = CreateWindow("Explorer", new Size(460, 340));

        upButton = window.CreateUIElement<Button>(b =>
        {
            b.Text = "Up";
            b.Position = new Point(Margin, ToolbarTop);
            b.Size = new Size(44, ToolbarHeight);
        });
        upButton.OnClick += (_, _) => GoUp();

        pathText = window.CreateUIElement<Text>(t =>
        {
            t.Color = TextColor;
            t.FontSize = FontSize;
            t.Position = new Point(Margin + 44 + 8, ToolbarTop + 2);
        });

        statusText = window.CreateUIElement<Text>(t =>
        {
            t.Color = StatusColor;
            t.FontSize = FontSize;
        });

        scrollBar = window.CreateUIElement<ScrollBar>(b => b.BackgroundColor = Color.FromArgb(48, 48, 48));
        scrollBar.Scrolled += value =>
        {
            top = value;
            displayDirty = true;
        };

        window.OnKeyEvent += HandleKey;
        window.OnMouseEvent += HandleMouse;

        ShowFolder(args.Length > 0 ? args[0] : WorkingDirectory, selectName: null);
    }

    internal override void Tick()
    {
        if (window.Size != lastSize && !window.IsResizing)
        {
            lastSize = window.Size;
            displayDirty = true;
        }

        if (displayDirty)
        {
            displayDirty = false;
            UpdateDisplay();
        }
    }

    internal override void Stop()
    {
        window.OnKeyEvent -= HandleKey;
        window.OnMouseEvent -= HandleMouse;
    }

    // --- Folder contents ---

    /// <summary>One line of the list: a subfolder or a file.</summary>
    private sealed record Entry(string Name, bool IsFolder, long Size);

    /// <summary>
    /// Shows <paramref name="path"/>, selecting the entry named <paramref name="selectName"/>
    /// (the folder just left, when going up) or else the first. A folder that cannot be read
    /// leaves the current one shown and says why.
    /// </summary>
    private void ShowFolder(string path, string? selectName)
    {
        if (!FileSystemManager.IsMounted)
        {
            entries = [];
            SetMessage(FileSystemManager.NoDiskMessage, isError: true);
            return;
        }

        List<Entry> read;
        try
        {
            DirectoryInfo directory = new(path);
            if (!directory.Exists)
            {
                SetMessage("No such folder: " + path, isError: true);
                return;
            }

            read = [
                .. directory.GetDirectories()
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(d => new Entry(d.Name, IsFolder: true, 0)),
                .. directory.GetFiles()
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new Entry(f.Name, IsFolder: false, f.Length)),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetMessage("Cannot open " + path + ": " + ex.Message, isError: true);
            return;
        }

        folder = path;
        entries = read;
        selected = Math.Max(0, selectName is null ? 0 : entries.FindIndex(e => e.Name.Equals(selectName, StringComparison.OrdinalIgnoreCase)));
        top = 0;
        lastClickedIndex = -1;
        window.Title = "Explorer - " + folder;
        message = null;
        ScrollToSelected();
        displayDirty = true;
    }

    private void GoUp()
    {
        if (Path.GetDirectoryName(folder) is { } parent)
        {
            ShowFolder(parent, selectName: Path.GetFileName(folder));
        }
    }

    /// <summary>Opens the selected entry: shows a folder, starts a file the way the start menu would.</summary>
    private void OpenSelected()
    {
        if (selected < 0 || selected >= entries.Count)
        {
            return;
        }

        Entry entry = entries[selected];
        string path = Path.Combine(folder, entry.Name);

        if (entry.IsFolder)
        {
            ShowFolder(path, selectName: null);
            return;
        }

        if (!FileTypes.CanOpen(path))
        {
            string extension = Path.GetExtension(entry.Name);
            SetMessage(extension.Length < 2
                ? "Files without an extension cannot be opened"
                : $"Nothing opens {extension} files (setting {FileTypes.AssociationKeyPrefix}{extension[1..].ToLowerInvariant()})", isError: true);
            return;
        }

        ProgramStart start = Launcher.Start(path, [], folder);
        if (start.Error is { } error)
        {
            SetMessage(error, isError: true);
        }
        else
        {
            SetMessage("Opened " + entry.Name, isError: false);
        }
    }

    private void SetMessage(string text, bool isError)
    {
        message = text;
        messageIsError = isError;
        displayDirty = true;
    }

    // --- Input ---

    private void HandleKey(KeyEvent keyEvent)
    {
        int page = Math.Max(1, VisibleRowCount - 1);

        switch (keyEvent.Key)
        {
            case Key.UpArrow:
                Select(selected - 1);
                break;
            case Key.DownArrow:
                Select(selected + 1);
                break;
            case Key.PageUp:
                Select(selected - page);
                break;
            case Key.PageDown:
                Select(selected + page);
                break;
            case Key.Home:
                Select(0);
                break;
            case Key.End:
                Select(entries.Count - 1);
                break;
            case Key.Enter or Key.NumEnter:
                OpenSelected();
                break;
            case Key.Backspace:
                GoUp();
                break;
            case Key.F5:
                ShowFolder(folder, selected < entries.Count ? entries[selected].Name : null);
                break;
        }
    }

    private void HandleMouse(MouseEvent mouseEvent)
    {
        // Window mouse events are in screen coordinates.
        int x = mouseEvent.X - window.Position.X;
        int y = mouseEvent.Y - window.Position.Y;

        if (mouseEvent.Type == MouseEventType.Wheel && mouseEvent.Delta != 0)
        {
            // The wheel's delta is in notches, positive downwards.
            ScrollTo(top + (mouseEvent.Delta * 3));
            return;
        }

        if (mouseEvent.Type != MouseEventType.ButtonDown || mouseEvent.Button != MouseButton.Left
            || x < 0 || x >= ListWidth || y < ListTop || y >= ListTop + (VisibleRowCount * RowHeight))
        {
            return;
        }

        int index = top + ((y - ListTop) / RowHeight);
        if (index >= entries.Count)
        {
            return;
        }

        long now = Environment.TickCount64;
        bool doubleClick = index == lastClickedIndex && now - lastClickTime <= DoubleClickMilliseconds;
        lastClickedIndex = doubleClick ? -1 : index;
        lastClickTime = now;

        Select(index);
        if (doubleClick)
        {
            OpenSelected();
        }
    }

    private void Select(int index)
    {
        if (entries.Count == 0)
        {
            return;
        }

        selected = Math.Clamp(index, 0, entries.Count - 1);
        ScrollToSelected();
        displayDirty = true;
    }

    /// <summary>Scrolls just enough to bring the selected entry into view.</summary>
    private void ScrollToSelected()
    {
        int visible = VisibleRowCount;
        if (selected < top)
        {
            ScrollTo(selected);
        }
        else if (selected >= top + visible)
        {
            ScrollTo(selected - visible + 1);
        }
    }

    private void ScrollTo(int first)
    {
        int scrolled = Math.Clamp(first, 0, Math.Max(0, entries.Count - VisibleRowCount));
        if (scrolled != top)
        {
            top = scrolled;
            displayDirty = true;
        }
    }

    // --- Display ---

    private int ListWidth => Math.Max(0, window.Size.Width - ScrollBarWidth - ScrollBarGap - Margin);

    private int ListBottom => window.Size.Height - StatusHeight - Margin;

    private int VisibleRowCount => Math.Max(1, (ListBottom - ListTop) / RowHeight);

    private static int CellWidth => CanvasExtensions.GetCellWidth(FontSize);

    private void UpdateDisplay()
    {
        int visible = VisibleRowCount;
        int listWidth = ListWidth;
        int columns = Math.Max(1, (listWidth - (2 * Margin)) / CellWidth);

        // A larger window shows more rows: keep the list filled to the end.
        top = Math.Clamp(top, 0, Math.Max(0, entries.Count - visible));

        pathText.Content = Fit(folder, Math.Max(1, (window.Size.Width - pathText.Position.X - Margin) / CellWidth));

        while (rows.Count < visible)
        {
            // The highlight first, so the text draws over it.
            Shapes.Rectangle highlight = window.CreateUIElement<Shapes.Rectangle>(r => r.Color = SelectedColor);
            Text text = window.CreateUIElement<Text>(t => t.FontSize = FontSize);
            rows.Add((highlight, text));
        }

        for (int i = 0; i < rows.Count; i++)
        {
            (Shapes.Rectangle highlight, Text text) = rows[i];
            int index = top + i;
            int y = ListTop + (i * RowHeight);

            if (i >= visible || index >= entries.Count)
            {
                highlight.Size = Size.Empty;
                text.Content = string.Empty;
                continue;
            }

            Entry entry = entries[index];
            bool isSelected = index == selected;
            highlight.Position = new Point(Margin, y);
            highlight.Size = isSelected ? new Size(listWidth - Margin, RowHeight) : Size.Empty;

            // A changed element is drawn last: send the text again too, so it stays over its highlight.
            if (highlight.AnyPropertyChanged)
            {
                text.Content = "\u0001";
            }

            text.Position = new Point(2 * Margin, y + 2);
            text.Color = isSelected ? Color.White : entry.IsFolder ? FolderColor : TextColor;
            text.Content = FormatRow(entry, columns);
        }

        scrollBar.Position = new Point(window.Size.Width - ScrollBarWidth - ScrollBarGap, ListTop);
        scrollBar.Size = new Size(ScrollBarWidth, Math.Max(0, visible * RowHeight));
        scrollBar.Maximum = entries.Count;
        scrollBar.ViewportSize = visible;
        scrollBar.Value = top;

        statusText.Position = new Point(Margin, window.Size.Height - StatusHeight);
        statusText.Color = message is not null && messageIsError ? ErrorColor : StatusColor;
        statusText.Content = Fit(message ?? CountText(), Math.Max(1, (window.Size.Width - (2 * Margin)) / CellWidth));

        window.Flush();
    }

    private string CountText()
    {
        int folders = entries.Count(e => e.IsFolder);
        int files = entries.Count - folders;
        return $"{folders} folder{(folders == 1 ? "" : "s")}, {files} file{(files == 1 ? "" : "s")}";
    }

    /// <summary>A row of the list: the name (a folder's with a slash) and, for a file, its size at the right.</summary>
    private static string FormatRow(Entry entry, int columns)
    {
        if (entry.IsFolder)
        {
            return Fit(entry.Name + "/", columns);
        }

        string size = FileCommandHelpers.FormatSize(entry.Size);
        int nameColumns = columns - size.Length - 1;
        if (nameColumns < 4)
        {
            return Fit(entry.Name, columns);
        }

        return Fit(entry.Name, nameColumns).PadRight(nameColumns + 1) + size;
    }

    /// <summary>Shortens <paramref name="text"/> to <paramref name="columns"/> characters, marking the cut with "~".</summary>
    private static string Fit(string text, int columns)
    {
        return text.Length <= columns ? text : columns <= 1 ? text[..columns] : text[..(columns - 1)] + "~";
    }
}
