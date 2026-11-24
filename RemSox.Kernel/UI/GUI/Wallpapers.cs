using System.Drawing;
using System.Reflection;

using RemSox.Kernel.Accounts;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;
using RemSox.Kernel.Processing;
using RemSox.Kernel.Security;
using RemSox.Shared.Imaging;

namespace RemSox.Kernel.UI.GUI;

/// <summary>What a background image is for.</summary>
public enum WallpaperRole
{
    /// <summary>Behind a desktop's windows.</summary>
    Desktop,

    /// <summary>Behind the login window: the lock screen, and the screen before anyone logs in.</summary>
    Lock,
}

/// <summary>
/// The background images, found like the cursors (<see cref="Cursors"/>): one file per role
/// (<c>desktop</c>, <c>lock</c>), a <c>.qoi</c> or <c>.bmp</c>, in
/// <see cref="SystemPaths.WallpapersFolder"/>, where the kernel writes its own (drawn by
/// <c>tools/make_wallpapers.py</c>) when none is there. An account replaces either with a file
/// of the same name in its own wallpapers folder (<see cref="Account.WallpapersFolder"/>).
/// </summary>
public static class Wallpapers
{
    /// <summary>The colour behind the windows where no image shows (none could be read, or while one loads).</summary>
    public static readonly Color BackgroundColor = Color.FromArgb(45, 45, 48);

    // The extensions tried for a role's file, in order.
    private static readonly string[] Extensions = [".qoi", ".bmp"];

    /// <summary>Gets the name of <paramref name="role"/>'s file, without an extension.</summary>
    public static string GetFileName(WallpaperRole role)
    {
        return role == WallpaperRole.Desktop ? "desktop" : "lock";
    }

    /// <summary>
    /// Writes the built-in images to <see cref="SystemPaths.WallpapersFolder"/> for each role with
    /// no file there in any format, so a replaced image (a <c>.bmp</c> for the built-in
    /// <c>.qoi</c>) stays.
    /// </summary>
    public static void WriteMissingFiles()
    {
        foreach (WallpaperRole role in (WallpaperRole[])[WallpaperRole.Desktop, WallpaperRole.Lock])
        {
            string name = GetFileName(role);
            if (FindFile(SystemPaths.WallpapersFolder, name, path => File.Exists(path)) is null && ReadResource(name) is { } file)
            {
                File.WriteAllBytes(SystemPaths.WallpapersFolder + "/" + name + ".qoi", file);
            }
        }
    }

    /// <summary>
    /// Loads <paramref name="role"/>'s image as <paramref name="caller"/>'s account sees it,
    /// scaled to fill <paramref name="size"/>: its own from its wallpapers folder (read with the
    /// caller's rights), else the system's, else the built-in one. A file that cannot be read or
    /// decoded is logged (as the account's, for its own) and the next one tried; null when none
    /// works. Files are read on the main loop, decoding and scaling run in the background.
    /// </summary>
    public static async Task<PixelImage?> LoadAsync(WallpaperRole role, Caller caller, Size size)
    {
        string name = GetFileName(role);

        if (caller.Account.WallpapersFolder is { } own
            && FindFile(own, name, path => UserFiles.Exists(caller, path)) is { } ownPath
            && await TryDecodeAsync(() => UserFiles.ReadAllBytes(caller, ownPath), ownPath, size, caller.Account) is { } ownImage)
        {
            return ownImage;
        }

        if (FindFile(SystemPaths.WallpapersFolder, name, path => File.Exists(path)) is { } systemPath
            && await TryDecodeAsync(() => File.ReadAllBytes(systemPath), systemPath, size, null) is { } systemImage)
        {
            return systemImage;
        }

        return await TryDecodeAsync(() => ReadResource(name), "the built-in " + name, size, null);
    }

    /// <summary>Returns the first of <paramref name="name"/>'s files in <paramref name="folder"/> that <paramref name="exists"/>, trying each extension.</summary>
    private static string? FindFile(string folder, string name, Func<string, bool> exists)
    {
        foreach (string extension in Extensions)
        {
            string path = folder + "/" + name + extension;
            try
            {
                if (exists(path))
                {
                    return path;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not there, as far as this caller may know.
            }
        }

        return null;
    }

    private static async Task<PixelImage?> TryDecodeAsync(Func<byte[]?> read, string what, Size size, Account? owner)
    {
        try
        {
            if (read() is not { } file)
            {
                return null;
            }

            return await BackgroundWork.Run(() => ImageFiles.Read(file).ScaleToFill(size.Width, size.Height));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            SystemLog.Log($"Could not use the background image {what}: {ex.Message}", LogSeverity.Warning, accountId: owner?.Id);
            return null;
        }
    }

    private static byte[]? ReadResource(string name)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Wallpapers." + name + ".qoi");
        if (stream is null)
        {
            SystemLog.Log($"The built-in background {name} is missing from the kernel.", LogSeverity.Error);
            return null;
        }

        byte[] file = new byte[stream.Length];
        stream.ReadExactly(file);
        return file;
    }
}
