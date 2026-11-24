using Cosmos.Kernel.System.Input;

using RemSox.Kernel.Processing;
using RemSox.Kernel.Security;
using RemSox.Kernel.UI.GUI;
using RemSox.Kernel.UI.GUI.UIEelements.Controls;
using RemSox.Kernel.UI.GUI.UIEelements.Shapes;
using RemSox.Kernel.UI.GUI.Windows;
using RemSox.Shared.Imaging;

using System.Drawing;

using Rectangle = RemSox.Kernel.UI.GUI.UIEelements.Shapes.Rectangle;

namespace RemSox.Kernel.Processes;

/// <summary>
/// The image viewer (<c>imageview</c>): a window with the file's path, buttons for the previous
/// and next image in its folder and for making it your wallpaper or lock screen, the picture
/// fitted to the window, and a status line. The arrow keys go through the folder too.
/// </summary>
/// <remarks>
/// It shows what <see cref="ImageFiles.Read"/> reads, BMP and QOI. Files are read through
/// <see cref="UserFiles"/> as the user who started it, and decoded and scaled on a thread of
/// their own (<see cref="BackgroundWork"/>), so a large image does not stop the system; the
/// picture is never enlarged past its own size. "Wallpaper" and "Lock screen" copy the file
/// into the account's wallpapers folder (<see cref="Accounts.Account.WallpapersFolder"/>), where
/// it replaces the system's, and show it at once.
/// </remarks>
public sealed class ImageViewProcess() : Process("Image Viewer")
{
    private const int Margin = 4;
    private const int RowHeight = 22;
    private const int ToolbarTop = Window.TitleBarHeight + Margin;
    private const int AreaTop = ToolbarTop + RowHeight + Margin;
    private const int StepWidth = 28;
    private const int WallpaperWidth = 84;
    private const int LockWidth = 100;
    private const int StatusFontSize = 16;

    private static readonly Color AreaColor = Color.FromArgb(24, 24, 24);
    private static readonly Color StatusColor = Color.FromArgb(160, 160, 160);
    private static readonly Color ErrorColor = Color.FromArgb(255, 110, 100);

    private Window window = null!;
    private TextBox pathBox = null!;
    private Button previousButton = null!;
    private Button nextButton = null!;
    private Button wallpaperButton = null!;
    private Button lockButton = null!;
    private Rectangle area = null!;
    private Picture picture = null!;
    private Text statusText = null!;

    // The file shown and its decoded image (transparency flattened), kept to fit it again when
    // the window changes size; the generation tells a finished load from one overtaken since.
    private string? shownPath;
    private PixelImage? image;
    private int generation;
    private Size lastSize = new(-1, -1);

    /// <param name="args">The image to show, as a full path (the <c>imageview</c> program's <c>Prepare</c> makes it one); none for an empty viewer.</param>
    internal override void Start(string[] args)
    {
        window = CreateWindow("Image Viewer", new Size(640, 480));
        window.AutoFlush = true;

        pathBox = window.CreateUIElement<TextBox>(b =>
        {
            b.BackgroundColor = Color.FromArgb(20, 20, 20);
            b.Position = new Point(Margin, ToolbarTop);
        });
        pathBox.Submitted += path => Open(path);

        previousButton = window.CreateUIElement<Button>(b => b.Text = "<");
        previousButton.OnClick += (_, _) => Step(-1);
        nextButton = window.CreateUIElement<Button>(b => b.Text = ">");
        nextButton.OnClick += (_, _) => Step(1);
        wallpaperButton = window.CreateUIElement<Button>(b => b.Text = "Wallpaper");
        wallpaperButton.OnClick += (_, _) => UseAs(WallpaperRole.Desktop);
        lockButton = window.CreateUIElement<Button>(b => b.Text = "Lock screen");
        lockButton.OnClick += (_, _) => UseAs(WallpaperRole.Lock);

        area = window.CreateUIElement<Rectangle>(r =>
        {
            r.Position = new Point(Margin, AreaTop);
            r.Color = AreaColor;
        });
        picture = window.CreateUIElement<Picture>(_ => { });

        statusText = window.CreateUIElement<Text>(t =>
        {
            t.Color = StatusColor;
            t.FontSize = StatusFontSize;
        });

        window.OnKeyEvent += HandleKey;

        Layout();
        if (args.Length > 0)
        {
            Open(args[0]);
        }
        else
        {
            pathBox.Text = WorkingDirectory.TrimEnd('/') + "/";
            SetStatus("Type the path of a .bmp or .qoi image above and press Enter.", isError: false);
            window.Focus(pathBox);
        }
    }

    internal override void Tick()
    {
        if (window.Size != lastSize && !window.IsResizing)
        {
            Layout();
            _ = FitAsync();
        }
    }

    internal override void Stop()
    {
        window.OnKeyEvent -= HandleKey;
        generation++;
    }

    private Size AreaSize => new(Math.Max(1, window.Size.Width - (2 * Margin)), Math.Max(1, window.Size.Height - AreaTop - RowHeight - (2 * Margin)));

    private void Layout()
    {
        lastSize = window.Size;
        int width = window.Size.Width;

        int buttonsWidth = (2 * StepWidth) + WallpaperWidth + LockWidth + (4 * Margin);
        pathBox.Size = new Size(Math.Max(40, width - (2 * Margin) - buttonsWidth), RowHeight);

        int x = width - Margin - buttonsWidth + Margin;
        foreach ((Button button, int buttonWidth) in (ReadOnlySpan<(Button, int)>)[(previousButton, StepWidth), (nextButton, StepWidth), (wallpaperButton, WallpaperWidth), (lockButton, LockWidth)])
        {
            button.Position = new Point(x, ToolbarTop);
            button.Size = new Size(buttonWidth, RowHeight);
            x += buttonWidth + Margin;
        }

        area.Size = AreaSize;
        statusText.Position = new Point(Margin, AreaTop + AreaSize.Height + Margin);
        statusText.MaxWidth = Math.Max(0, width - (2 * Margin));
    }

    /// <summary>Reads and shows the image at <paramref name="path"/> (relative to the starting folder).</summary>
    private void Open(string path)
    {
        try
        {
            path = Path.GetFullPath(path.Trim(), WorkingDirectory).Replace('\\', '/');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            SetStatus("Not a path: " + ex.Message, isError: true);
            return;
        }

        pathBox.Text = path;
        _ = LoadAsync(path);
    }

    private async Task LoadAsync(string path)
    {
        int load = ++generation;
        byte[] file;
        try
        {
            file = UserFiles.ReadAllBytes(Caller, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus("Cannot read the file: " + ex.Message, isError: true);
            return;
        }

        SetStatus("Opening...", isError: false);
        Size size = AreaSize;
        try
        {
            (PixelImage decoded, PixelImage fitted) = await BackgroundWork.Run(() =>
            {
                PixelImage read = ImageFiles.Read(file).Flatten((uint)AreaColor.ToArgb());
                return (read, read.ScaleToFit(size.Width, size.Height));
            });
            if (load != generation)
            {
                return;
            }

            shownPath = path;
            image = decoded;
            Show(fitted);
        }
        catch (InvalidDataException ex)
        {
            if (load == generation)
            {
                SetStatus("Not an image RemSox can show (BMP or QOI): " + ex.Message, isError: true);
            }
        }
    }

    /// <summary>Fits the image shown to the window again, after it changed size.</summary>
    private async Task FitAsync()
    {
        if (image is not { } shown)
        {
            return;
        }

        int fit = ++generation;
        Size size = AreaSize;
        PixelImage fitted = await BackgroundWork.Run(() => shown.ScaleToFit(size.Width, size.Height));
        if (fit == generation)
        {
            Show(fitted);
        }
    }

    private void Show(PixelImage fitted)
    {
        Size size = AreaSize;
        picture.Position = new Point(Margin + ((size.Width - fitted.Width) / 2), AreaTop + ((size.Height - fitted.Height) / 2));
        picture.Image = fitted;

        string name = Path.GetFileName(shownPath ?? string.Empty);
        window.Title = name + " - Image Viewer";
        window.Flush();
        int percent = (int)((long)fitted.Width * 100 / Math.Max(1, image!.Width));
        SetStatus($"{name}: {image.Width} x {image.Height}, shown at {percent}%", isError: false);
    }

    /// <summary>Opens the previous (-1) or next (1) image in the folder of the one shown, wrapping around.</summary>
    private void Step(int direction)
    {
        if (shownPath is null)
        {
            return;
        }

        string folder = Path.GetDirectoryName(shownPath)?.Replace('\\', '/') ?? "/";
        List<string> images;
        try
        {
            images = [.. UserFiles.List(Caller, folder).Files
                .Select(file => file.Name)
                .Where(IsImageName)
                .Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Cannot list the folder: " + ex.Message, isError: true);
            return;
        }

        if (images.Count == 0)
        {
            return;
        }

        int index = images.FindIndex(name => string.Equals(name, Path.GetFileName(shownPath), StringComparison.OrdinalIgnoreCase));
        int next = index < 0 ? 0 : (index + direction + images.Count) % images.Count;
        Open(folder.TrimEnd('/') + "/" + images[next]);
    }

    private static bool IsImageName(string name)
    {
        return name.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".qoi", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Makes the image shown the account's desktop background or lock screen: copies it into
    /// its wallpapers folder under the role's name (removing one of the other format, which
    /// would come first), and has its screens show it.
    /// </summary>
    private void UseAs(WallpaperRole role)
    {
        if (shownPath is null || !IsImageName(shownPath))
        {
            SetStatus("Open a .bmp or .qoi image first.", isError: true);
            return;
        }

        if (Caller.Account.WallpapersFolder is not { } folder)
        {
            SetStatus("This account has no wallpapers of its own.", isError: true);
            return;
        }

        string name = Wallpapers.GetFileName(role);
        string extension = Path.GetExtension(shownPath).ToLowerInvariant();
        string other = extension == ".qoi" ? ".bmp" : ".qoi";
        try
        {
            if (UserFiles.Exists(Caller, folder + "/" + name + other))
            {
                UserFiles.Delete(Caller, folder + "/" + name + other, recursive: false);
            }

            UserFiles.CopyFile(Caller, shownPath, folder + "/" + name + extension, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus("Could not copy it: " + ex.Message, isError: true);
            return;
        }

        Screen.ReloadPersonalization(Caller.Account);
        SetStatus(role == WallpaperRole.Desktop ? "It is your wallpaper now." : "It is your lock screen now.", isError: false);
    }

    private void HandleKey(KeyEvent keyEvent)
    {
        if (pathBox.IsFocused)
        {
            return;
        }

        if (keyEvent.Key == Key.LeftArrow)
        {
            Step(-1);
        }
        else if (keyEvent.Key == Key.RightArrow)
        {
            Step(1);
        }
    }

    private void SetStatus(string message, bool isError)
    {
        statusText.Content = message;
        statusText.Color = isError ? ErrorColor : StatusColor;
    }
}
