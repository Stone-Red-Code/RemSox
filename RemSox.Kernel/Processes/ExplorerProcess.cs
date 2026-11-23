using Cosmos.Kernel.System.Input;

using RemSox.Kernel.Configuration;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Processing;
using RemSox.Kernel.Security;
using RemSox.Kernel.Programs;
using RemSox.Kernel.UI.CLI;
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
/// so scripts, shortcuts and associated files behave the same everywhere. It also creates,
/// renames, copies, moves and deletes folders and files, opens a file with a program chosen by
/// name, and opens a Terminal in the folder shown.
/// </summary>
/// <remarks>
/// The list is drawn like the Terminal's output: only the rows in view have elements, so a
/// folder of any size costs the same to show and nothing needs clipping. What an action needs
/// from the user (a name, a yes) is asked in a row between the list and the status line.
/// </remarks>
public sealed class ExplorerProcess() : Process("Explorer")
{
    private const int Margin = 4;
    private const int ToolbarTop = Window.TitleBarHeight + Margin;
    private const int ToolbarHeight = 22;
    private const int SecondToolbarTop = ToolbarTop + ToolbarHeight + Margin;
    private const int PathTop = SecondToolbarTop + ToolbarHeight + Margin;
    private const int RowHeight = 22;
    private const int ListTop = PathTop + RowHeight;
    private const int FontSize = 18;
    private const int StatusHeight = 22;
    private const int ScrollBarWidth = 10;
    private const int ScrollBarGap = 6;
    private const long DoubleClickMilliseconds = 500;
    private const int ActionLineHeight = ToolbarHeight + Margin;

    // What Open with suggests for a file nothing opens yet.
    private const string DefaultOpener = BuiltInPrograms.TextEditName;

    // Characters a name may not contain: the path separators, and what FAT and Windows refuse.
    private static readonly char[] InvalidNameChars = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly Color TextColor = Color.FromArgb(220, 220, 220);
    private static readonly Color FolderColor = Color.FromArgb(120, 180, 255);
    private static readonly Color SelectedColor = Color.FromArgb(0, 90, 160);
    private static readonly Color StatusColor = Color.FromArgb(160, 160, 160);
    private static readonly Color ErrorColor = Color.FromArgb(255, 110, 100);

    /// <summary>What the action row is asking for.</summary>
    private enum Prompt
    {
        None,
        NewFolder,
        NewFile,
        Rename,
        Delete,
        OpenWith,
    }

    private Window window = null!;
    private Text pathText = null!;
    private Text statusText = null!;
    private ScrollBar scrollBar = null!;
    private readonly List<(Shapes.Rectangle Highlight, Text Text)> rows = [];

    // The action row: a label, then a text box (names, programs) or Yes/No buttons (delete),
    // and for Open with a second line with the "always" box.
    private Text actionLabel = null!;
    private TextBox actionBox = null!;
    private Button confirmButton = null!;
    private Button cancelButton = null!;
    private CheckBox alwaysBox = null!;

    private string folder = "/";
    private List<Entry> entries = [];
    private int selected;
    private int top;
    private string? message;
    private bool messageIsError;

    private Prompt action;

    // The entry Rename, Delete and Open with act on, kept from when the row opened.
    private Entry? actionEntry;

    /// <summary>What Copy or Cut took, waiting for Paste: shared by every Explorer window, like a clipboard.</summary>
    private sealed record Clipboard(string Path, bool IsFolder, bool Cut);

    private static Clipboard? clipboard;

    private Size lastSize = new(-1, -1);
    private bool displayDirty = true;

    // The last click on the list, to tell a double-click.
    private int lastClickedIndex = -1;
    private long lastClickTime;

    /// <param name="args">The folder to show, already checked by the <c>explorer</c> program's <c>Prepare</c>; none for the working directory.</param>
    internal override void Start(string[] args)
    {
        window = CreateWindow("Explorer", new Size(520, 360));

        int x = Margin;
        AddToolbarButton("Up", 44, ref x).OnClick += (_, _) =>
        {
            CancelAction();
            GoUp();
        };
        AddToolbarButton("New folder", 96, ref x).OnClick += (_, _) => StartNaming(Prompt.NewFolder);
        AddToolbarButton("New file", 80, ref x).OnClick += (_, _) => StartNaming(Prompt.NewFile);
        AddToolbarButton("Rename", 64, ref x).OnClick += (_, _) => StartRename();
        AddToolbarButton("Delete", 64, ref x).OnClick += (_, _) => StartDelete();

        x = Margin;
        AddToolbarButton("Copy", 52, ref x, SecondToolbarTop).OnClick += (_, _) => CopySelected(cut: false);
        AddToolbarButton("Cut", 44, ref x, SecondToolbarTop).OnClick += (_, _) => CopySelected(cut: true);
        AddToolbarButton("Paste", 56, ref x, SecondToolbarTop).OnClick += (_, _) => Paste();
        AddToolbarButton("Open with", 88, ref x, SecondToolbarTop).OnClick += (_, _) => StartOpenWith(null);
        AddToolbarButton("Terminal", 80, ref x, SecondToolbarTop).OnClick += (_, _) => OpenTerminalHere();

        pathText = window.CreateUIElement<Text>(t =>
        {
            t.Color = TextColor;
            t.FontSize = FontSize;
            t.Position = new Point(Margin, PathTop);
        });

        actionLabel = window.CreateUIElement<Text>(t =>
        {
            t.Color = TextColor;
            t.FontSize = FontSize;
        });

        actionBox = window.CreateUIElement<TextBox>(b => b.BackgroundColor = Color.FromArgb(20, 20, 20));
        actionBox.Submitted += SubmitAction;
        actionBox.Cancelled += CancelAction;

        confirmButton = window.CreateUIElement<Button>(b => b.Text = "Delete");
        confirmButton.OnClick += (_, _) => ConfirmDelete();

        cancelButton = window.CreateUIElement<Button>(b => b.Text = "Cancel");
        cancelButton.OnClick += (_, _) => CancelAction();

        alwaysBox = window.CreateUIElement<CheckBox>(b =>
        {
            b.BackgroundColor = Color.FromArgb(220, 220, 220);
            b.TextColor = TextColor;
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

    private Button AddToolbarButton(string text, int width, ref int x, int y = ToolbarTop)
    {
        int left = x;
        x += width + Margin;
        return window.CreateUIElement<Button>(b =>
        {
            b.Text = text;
            b.Position = new Point(left, y);
            b.Size = new Size(width, ToolbarHeight);
        });
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

    private Entry? SelectedEntry => selected >= 0 && selected < entries.Count ? entries[selected] : null;

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
            (DirectoryInfo[] folders, FileInfo[] files) = UserFiles.List(Caller, path);

            read = [
                .. folders
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(d => new Entry(d.Name, IsFolder: true, 0)),
                .. files
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

    /// <summary>
    /// Opens the selected entry: shows a folder, starts a file the way the start menu would. A
    /// file nothing opens asks which program should.
    /// </summary>
    private void OpenSelected()
    {
        if (SelectedEntry is not { } entry)
        {
            return;
        }

        string path = Path.Combine(folder, entry.Name);

        if (entry.IsFolder)
        {
            ShowFolder(path, selectName: null);
            return;
        }

        if (!FileTypes.CanOpen(path, Caller.Account))
        {
            StartOpenWith(Path.GetExtension(entry.Name).Length < 2
                ? "Nothing opens files without an extension: choose a program"
                : $"Nothing opens {Path.GetExtension(entry.Name)} files yet: choose a program");
            return;
        }

        Report(Launcher.Start(path, [], folder, Caller), entry.Name);
    }

    private void Report(ProgramStart start, string name)
    {
        if (start.Error is { } error)
        {
            SetMessage(error, isError: true);
        }
        else
        {
            SetMessage("Opened " + name, isError: false);
        }
    }

    private void SetMessage(string text, bool isError)
    {
        message = text;
        messageIsError = isError;
        displayDirty = true;
    }

    // --- The action row ---

    private void OpenAction(Prompt next, string? label = null)
    {
        action = next;
        actionLabel.Content = label ?? string.Empty;
        ScrollToSelected();
        displayDirty = true;
    }

    /// <summary>Closes the action row without doing anything.</summary>
    private void CancelAction()
    {
        if (action == Prompt.None)
        {
            return;
        }

        action = Prompt.None;
        actionEntry = null;
        window.Focus(null);
        displayDirty = true;
    }

    private void SubmitAction(string text)
    {
        switch (action)
        {
            case Prompt.NewFolder or Prompt.NewFile:
                Create(text);
                break;
            case Prompt.OpenWith:
                OpenWith(text);
                break;
            case Prompt.Rename:
                Rename(text);
                break;
        }
    }

    // --- Creating ---

    /// <summary>Asks for the name of a new folder or file.</summary>
    private void StartNaming(Prompt kind)
    {
        if (!FileSystemManager.IsMounted)
        {
            SetMessage(FileSystemManager.NoDiskMessage, isError: true);
            return;
        }

        CancelAction();
        actionBox.Text = string.Empty;
        window.Focus(actionBox);
        message = null;
        OpenAction(kind, kind == Prompt.NewFolder ? "Folder name:" : "File name:");
    }

    /// <summary>
    /// Creates the folder or empty file named <paramref name="typed"/> in the folder shown, and
    /// selects it. A name that cannot be used keeps the row open with the reason below.
    /// </summary>
    private void Create(string typed)
    {
        bool folderWanted = action == Prompt.NewFolder;
        string name = typed.Trim();

        if (ValidateName(name) is { } problem)
        {
            SetMessage(problem, isError: true);
            return;
        }

        string path = Path.Combine(folder, name);

        try
        {
            if (UserFiles.Exists(Caller, path))
            {
                SetMessage(name + " already exists", isError: true);
                return;
            }

            if (folderWanted)
            {
                UserFiles.CreateDirectory(Caller, path);
            }
            else
            {
                UserFiles.CreateFile(Caller, path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetMessage("Cannot create " + name + ": " + ex.Message, isError: true);
            return;
        }

        CancelAction();
        ShowFolder(folder, selectName: name);
        SetMessage("Created " + name + (folderWanted ? "/" : string.Empty), isError: false);
    }

    /// <summary>Returns why <paramref name="name"/> cannot name a file or folder, or null when it can.</summary>
    private static string? ValidateName(string name)
    {
        if (name.Length == 0)
        {
            return "Type a name first";
        }

        if (name is "." or "..")
        {
            return name + " cannot be a name";
        }

        int invalid = name.IndexOfAny(InvalidNameChars);
        if (invalid >= 0)
        {
            return "A name cannot contain " + name[invalid];
        }

        return name.Any(char.IsControl) ? "A name cannot contain control characters" : null;
    }

    // --- Renaming ---

    /// <summary>Asks for a new name for the selected entry, starting from its current one.</summary>
    private void StartRename()
    {
        if (SelectedEntry is not { } entry)
        {
            SetMessage("Select something to rename", isError: true);
            return;
        }

        CancelAction();
        actionEntry = entry;
        actionBox.Text = entry.Name;
        window.Focus(actionBox);
        message = null;
        OpenAction(Prompt.Rename, "New name:");
    }

    /// <summary>Renames the entry to <paramref name="typed"/>; a name in use keeps the row open.</summary>
    private void Rename(string typed)
    {
        if (actionEntry is not { } entry)
        {
            return;
        }

        string name = typed.Trim();
        if (name == entry.Name)
        {
            CancelAction();
            return;
        }

        if (ValidateName(name) is { } problem)
        {
            SetMessage(problem, isError: true);
            return;
        }

        string source = Path.Combine(folder, entry.Name);
        string destination = Path.Combine(folder, name);

        // FAT ignores case: a name that differs only in case is the same entry, not another one.
        bool caseOnly = name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase);

        try
        {
            if (!caseOnly && UserFiles.Exists(Caller, destination))
            {
                SetMessage(name + " already exists", isError: true);
                return;
            }

            if (caseOnly)
            {
                // Through a name of its own, since moving onto itself does nothing.
                string temporary = Path.Combine(folder, "~rename~" + Environment.TickCount64);
                UserFiles.Move(Caller, source, temporary, overwrite: false);
                UserFiles.Move(Caller, temporary, destination, overwrite: false);
            }
            else
            {
                UserFiles.Move(Caller, source, destination, overwrite: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetMessage("Cannot rename " + entry.Name + ": " + ex.Message, isError: true);
            return;
        }

        CancelAction();
        ShowFolder(folder, selectName: name);
        SetMessage("Renamed " + entry.Name + " to " + name, isError: false);
    }


    // --- Copy, cut and paste ---

    /// <summary>Remembers the selected entry for <see cref="Paste"/>, to copy it or (cut) to move it.</summary>
    private void CopySelected(bool cut)
    {
        CancelAction();

        if (SelectedEntry is not { } entry)
        {
            SetMessage("Select something to " + (cut ? "cut" : "copy"), isError: true);
            return;
        }

        clipboard = new Clipboard(Path.Combine(folder, entry.Name), entry.IsFolder, cut);
        SetMessage($"{(cut ? "Cut" : "Copied")} {entry.Name}{(entry.IsFolder ? "/" : string.Empty)}: open a folder and paste (Ctrl+V)", isError: false);
    }

    /// <summary>
    /// Copies or moves what Copy or Cut took into the folder shown. A copy into a folder that
    /// already has that name is named "name - Copy" (then "name - Copy (2)", ...); a move never
    /// replaces anything.
    /// </summary>
    private void Paste()
    {
        CancelAction();

        if (clipboard is not { } taken)
        {
            SetMessage("Nothing to paste: copy or cut something first", isError: true);
            return;
        }

        string name = Path.GetFileName(taken.Path);

        try
        {
            if (!UserFiles.Exists(Caller, taken.Path))
            {
                clipboard = null;
                SetMessage(taken.Path + " is gone", isError: true);
                return;
            }

            // A folder cannot go inside itself: copying would never end, moving would cut it off.
            if (taken.IsFolder && IsSameOrInside(folder, taken.Path))
            {
                SetMessage("Cannot paste " + name + "/ into itself", isError: true);
                return;
            }

            string destination = Path.Combine(folder, name);

            if (taken.Cut)
            {
                if (destination.Equals(taken.Path, StringComparison.OrdinalIgnoreCase))
                {
                    SetMessage(name + " is already here", isError: true);
                    return;
                }

                if (UserFiles.Exists(Caller, destination))
                {
                    SetMessage(name + " already exists here", isError: true);
                    return;
                }

                UserFiles.Move(Caller, taken.Path, destination, overwrite: false);
                clipboard = null;
            }
            else
            {
                destination = FreeCopyName(name);
                if (taken.IsFolder)
                {
                    CopyFolder(taken.Path, destination);
                }
                else
                {
                    UserFiles.CopyFile(Caller, taken.Path, destination, overwrite: false);
                }
            }

            ShowFolder(folder, selectName: Path.GetFileName(destination));
            SetMessage($"{(taken.Cut ? "Moved" : "Copied")} {name} here", isError: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ShowFolder(folder, SelectedEntry?.Name);
            SetMessage("Cannot paste " + name + ": " + ex.Message, isError: true);
        }
    }

    /// <summary>Returns whether <paramref name="path"/> is <paramref name="folderPath"/> or a path inside it, ignoring case as FAT does.</summary>
    private static bool IsSameOrInside(string path, string folderPath)
    {
        return path.Equals(folderPath, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(folderPath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A path in the folder shown for a copy of <paramref name="name"/> that does not exist yet.</summary>
    private string FreeCopyName(string name)
    {
        string path = Path.Combine(folder, name);
        if (!UserFiles.Exists(Caller, path))
        {
            return path;
        }

        string stem = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);
        for (int i = 1; ; i++)
        {
            string candidate = Path.Combine(folder, $"{stem} - Copy{(i == 1 ? string.Empty : $" ({i})")}{extension}");
            if (!UserFiles.Exists(Caller, candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Copies a folder and everything in it to <paramref name="destination"/>, which must not exist.</summary>
    private void CopyFolder(string source, string destination)
    {
        UserFiles.CreateDirectory(Caller, destination);
        (DirectoryInfo[] folders, FileInfo[] files) = UserFiles.List(Caller, source);

        foreach (FileInfo file in files)
        {
            UserFiles.CopyFile(Caller, file.FullName, Path.Combine(destination, file.Name), overwrite: false);
        }

        foreach (DirectoryInfo subfolder in folders)
        {
            CopyFolder(subfolder.FullName, Path.Combine(destination, subfolder.Name));
        }
    }

    // --- Terminal ---

    /// <summary>Opens a Terminal whose shell starts in the folder shown.</summary>
    private void OpenTerminalHere()
    {
        CancelAction();

        ProgramStart start = ProgramRegistry.Find(BuiltInPrograms.TerminalName) is { } terminal
            ? ProgramRegistry.Start(terminal, [], folder, Caller)
            : ProgramStart.Fail("There is no Terminal program.");

        if (start.Error is { } error)
        {
            SetMessage(error, isError: true);
        }
        else
        {
            SetMessage("Opened a Terminal in " + folder, isError: false);
        }
    }

    // --- Deleting ---

    /// <summary>Asks whether to delete the selected entry; a folder goes with everything in it.</summary>
    private void StartDelete()
    {
        if (SelectedEntry is not { } entry)
        {
            SetMessage("Select something to delete", isError: true);
            return;
        }

        CancelAction();
        actionEntry = entry;
        window.Focus(null);

        bool hasContents = false;
        if (entry.IsFolder)
        {
            try
            {
                hasContents = UserFiles.HasEntries(Caller, Path.Combine(folder, entry.Name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Asked as if it had contents: deleting says what went wrong.
                hasContents = true;
            }
        }

        SetMessage("This cannot be undone. Enter deletes, Esc keeps it", isError: false);
        OpenAction(Prompt.Delete, hasContents
            ? $"Delete {entry.Name}/ and everything in it?"
            : $"Delete {entry.Name}{(entry.IsFolder ? "/" : string.Empty)}?");
    }

    private void ConfirmDelete()
    {
        if (action != Prompt.Delete || actionEntry is not { } entry)
        {
            return;
        }

        string path = Path.Combine(folder, entry.Name);

        // Afterwards select what took its place: the next entry, or the one before at the end.
        int index = entries.IndexOf(entry);
        string? neighbour = index + 1 < entries.Count ? entries[index + 1].Name : index > 0 ? entries[index - 1].Name : null;

        CancelAction();

        try
        {
            UserFiles.Delete(Caller, path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ShowFolder(folder, selectName: entry.Name);
            SetMessage("Cannot delete " + entry.Name + ": " + ex.Message, isError: true);
            return;
        }

        ShowFolder(folder, selectName: neighbour);
        SetMessage("Deleted " + entry.Name + (entry.IsFolder ? "/" : string.Empty), isError: false);
    }

    // --- Open with ---

    /// <summary>
    /// Asks which program or command should open the selected file, suggesting the one that
    /// opens it now (or <c>edit</c>). <paramref name="reason"/> says why it is asked, when the
    /// user did not ask.
    /// </summary>
    private void StartOpenWith(string? reason)
    {
        if (SelectedEntry is not { IsFolder: false } entry)
        {
            SetMessage("Select a file to open", isError: true);
            return;
        }

        CancelAction();
        actionEntry = entry;

        string extension = Path.GetExtension(entry.Name);
        alwaysBox.IsChecked = false;
        alwaysBox.Text = extension.Length < 2 || FileTypes.IsScript(entry.Name) || FileTypes.IsShortcut(entry.Name)
            ? string.Empty
            : $"Always open {extension} files this way";

        actionBox.Text = FileTypes.GetOpener(Path.Combine(folder, entry.Name), Caller.Account) ?? DefaultOpener;
        window.Focus(actionBox);

        if (reason is null)
        {
            message = null;
        }
        else
        {
            SetMessage(reason, isError: false);
        }

        OpenAction(Prompt.OpenWith, "Open with:");
    }

    /// <summary>
    /// Opens the file with <paramref name="typed"/>, a program or a command, the way
    /// <see cref="Launcher"/> opens any file. With the box ticked it becomes the file type's
    /// association for this account (its own setting, over the machine's).
    /// </summary>
    private void OpenWith(string typed)
    {
        if (actionEntry is not { } entry)
        {
            return;
        }

        string opener = typed.Trim();
        string path = Path.Combine(folder, entry.Name);
        string extension = Path.GetExtension(entry.Name);

        // An empty association is a valid setting ("no opener"), but here it can only be a slip:
        // saving it would take away the machine's opener for this type.
        if (opener.Length == 0)
        {
            SetMessage("Type the program or command to open it with", isError: true);
            return;
        }

        // Saving the association checks the opener too, so nothing is saved for one that fails.
        if (alwaysBox.IsChecked && alwaysBox.Text.Length > 0
            && SystemSettings.SetOwn(Caller, FileTypes.AssociationKeyPrefix + extension[1..].ToLowerInvariant(), opener) is { } saveError)
        {
            SetMessage(saveError, isError: true);
            return;
        }

        // On failure the box stays open with the reason, to try another.
        if (Launcher.OpenWith(opener, path, folder, Caller).Error is { } error)
        {
            SetMessage(error, isError: true);
            return;
        }

        CancelAction();
        SetMessage($"Opened {entry.Name} with {opener}", isError: false);
    }

    // --- Input ---

    private void HandleKey(KeyEvent keyEvent)
    {
        switch (action)
        {
            case Prompt.Delete:
                // Only a clear yes deletes.
                if (keyEvent.Key is Key.Enter or Key.NumEnter)
                {
                    ConfirmDelete();
                }
                else if (keyEvent.Key == Key.Escape)
                {
                    CancelAction();
                    message = null;
                }

                return;

            case not Prompt.None:
                // The text box has the keys while something is typed. A click elsewhere in the
                // window (the "always" box, say) took them from it, as the window focuses what is
                // clicked: hand them back, so typing and Enter still go to the row.
                if (!actionBox.IsFocused)
                {
                    window.Focus(actionBox);
                    actionBox.HandleKeyEvent(keyEvent);
                }

                return;
        }

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
            case Key.Delete:
                StartDelete();
                break;
            case Key.F2:
                StartRename();
                break;
            case Key.C when keyEvent.Modifiers.HasFlag(ConsoleModifiers.Control):
                CopySelected(cut: false);
                break;
            case Key.X when keyEvent.Modifiers.HasFlag(ConsoleModifiers.Control):
                CopySelected(cut: true);
                break;
            case Key.V when keyEvent.Modifiers.HasFlag(ConsoleModifiers.Control):
                Paste();
                break;
            case Key.F5:
                ShowFolder(folder, SelectedEntry?.Name);
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

        // A click on the list leaves the question unanswered.
        CancelAction();

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

    // Open with has a second line, for the "always" box.
    private int ActionHeight => action switch
    {
        Prompt.None => 0,
        Prompt.OpenWith when alwaysBox.Text.Length > 0 => 2 * ActionLineHeight,
        _ => ActionLineHeight,
    };

    private int ListBottom => window.Size.Height - StatusHeight - Margin - ActionHeight;

    private int VisibleRowCount => Math.Max(1, (ListBottom - ListTop) / RowHeight);

    private static int CellWidth => CanvasExtensions.GetCellWidth(FontSize);

    private void UpdateDisplay()
    {
        int visible = VisibleRowCount;
        int listWidth = ListWidth;
        int columns = Math.Max(1, (listWidth - (2 * Margin)) / CellWidth);

        // A larger window shows more rows: keep the list filled to the end.
        top = Math.Clamp(top, 0, Math.Max(0, entries.Count - visible));

        pathText.Content = Fit(folder, Math.Max(1, (window.Size.Width - (2 * Margin)) / CellWidth));

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

            text.Position = new Point(2 * Margin, y + 2);
            text.Color = isSelected ? Color.White : entry.IsFolder ? FolderColor : TextColor;
            text.Content = FormatRow(entry, columns);
        }

        scrollBar.Position = new Point(window.Size.Width - ScrollBarWidth - ScrollBarGap, ListTop);
        scrollBar.Size = new Size(ScrollBarWidth, Math.Max(0, visible * RowHeight));
        scrollBar.Maximum = entries.Count;
        scrollBar.ViewportSize = visible;
        scrollBar.Value = top;

        LayOutActionRow();

        statusText.Position = new Point(Margin, window.Size.Height - StatusHeight);
        statusText.Color = message is not null && messageIsError ? ErrorColor : StatusColor;
        statusText.Content = Fit(message ?? CountText(), Math.Max(1, (window.Size.Width - (2 * Margin)) / CellWidth));

        window.Flush();
    }

    /// <summary>Places the action row's parts for the current action, hiding the ones it does not use.</summary>
    private void LayOutActionRow()
    {
        int y = ListBottom + Margin;
        int right = window.Size.Width - Margin;
        bool typing = action is Prompt.NewFolder or Prompt.NewFile or Prompt.Rename or Prompt.OpenWith;
        bool confirming = action == Prompt.Delete;

        actionLabel.Position = new Point(Margin, y + 2);
        if (action == Prompt.None)
        {
            actionLabel.Content = string.Empty;
        }

        // The buttons go at the right; the question is cut short rather than run under them.
        int buttonsLeft = right - 64 - Margin - 64;
        if (confirming)
        {
            actionLabel.Content = Fit(actionLabel.Content, Math.Max(1, (buttonsLeft - (2 * Margin)) / CellWidth));
        }

        confirmButton.Position = new Point(buttonsLeft, y);
        confirmButton.Size = confirming ? new Size(64, ToolbarHeight) : Size.Empty;
        cancelButton.Position = new Point(right - 64, y);
        cancelButton.Size = confirming ? new Size(64, ToolbarHeight) : Size.Empty;

        int boxX = Margin + ((actionLabel.Content.Length + 1) * CellWidth);
        actionBox.Position = new Point(boxX, y);
        actionBox.Size = typing ? new Size(Math.Max(0, right - boxX), ToolbarHeight) : Size.Empty;

        bool always = action == Prompt.OpenWith && alwaysBox.Text.Length > 0;
        alwaysBox.Position = new Point(Margin, y + ActionLineHeight + 3);
        alwaysBox.Size = always ? new Size(Math.Max(0, right - Margin), 16) : Size.Empty;
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
