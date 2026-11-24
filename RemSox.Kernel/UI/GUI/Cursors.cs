using System.Reflection;

using RemSox.Kernel.Accounts;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;
using RemSox.Kernel.Security;
using RemSox.Shared.UI.GUI.Rendering;

namespace RemSox.Kernel.UI.GUI;

/// <summary>
/// The pointer images. A pointer is always an image file, one per Windows pointer role, in
/// <see cref="SystemPaths.CursorsFolder"/>: the kernel carries the set (Bibata Modern Ice,
/// <c>Resources/Cursors</c>) and writes each file that is missing there at boot. A cursor is
/// read from the disk, else from the built-in copy, when there is no disk or the file on it
/// cannot be read. An account replaces cursors one by one with files of the same name in its
/// own cursors folder (<see cref="Account.CursorsFolder"/>); what that folder lacks comes from
/// the system's set.
/// </summary>
public static class Cursors
{
    private static readonly Dictionary<CursorRole, CursorShape?> loaded = [];

    /// <summary>
    /// Returns the cursor for <paramref name="role"/>, read once at its first use. A cursor that
    /// cannot be read at all, neither the disk's file nor the built-in one, gives the normal
    /// pointer instead (null only when that cannot be read either).
    /// </summary>
    public static CursorShape? Get(CursorRole role)
    {
        if (!loaded.TryGetValue(role, out CursorShape? shape))
        {
            shape = Load(GetFileName(role)) ?? (role == CursorRole.Arrow ? null : Get(CursorRole.Arrow));
            loaded[role] = shape;
        }

        return shape;
    }

    /// <summary>
    /// Returns the cursor for <paramref name="role"/> as <paramref name="caller"/>'s account
    /// sees it: its own from its cursors folder, read with the caller's rights, else the
    /// system's (no file there for the role, or one that cannot be read: logged as the account's).
    /// Not cached: the caller keeps what it uses.
    /// </summary>
    public static CursorShape? Get(CursorRole role, Caller caller)
    {
        if (caller.Account.CursorsFolder is not { } folder)
        {
            return Get(role);
        }

        string path = folder + "/" + GetFileName(role) + ".cur";
        try
        {
            if (UserFiles.Exists(caller, path))
            {
                return CursorShape.FromCursorFile(UserFiles.ReadAllBytes(caller, path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // The account's own entry, so its own 'logs' shows why its cursor is not used.
            SystemLog.Log($"Could not read the cursor {path} of {caller.Account.Name}, using the system's: {ex.Message}", LogSeverity.Warning, accountId: caller.Account.Id);
        }

        return Get(role);
    }

    /// <summary>
    /// Forgets the system's cursors read so far, so each is read again at its next use, after
    /// files in <see cref="SystemPaths.CursorsFolder"/> changed.
    /// </summary>
    public static void Forget()
    {
        loaded.Clear();
    }

    /// <summary>Gets the name of <paramref name="role"/>'s file in the set, without <c>.cur</c>.</summary>
    public static string GetFileName(CursorRole role)
    {
        return role switch
        {
            CursorRole.Arrow => "arrow",
            CursorRole.Help => "help",
            CursorRole.Working => "working",
            CursorRole.Busy => "busy",
            CursorRole.Crosshair => "crosshair",
            CursorRole.Text => "text",
            CursorRole.Pen => "pen",
            CursorRole.Unavailable => "unavailable",
            CursorRole.ResizeNorthSouth => "resize-ns",
            CursorRole.ResizeEastWest => "resize-ew",
            CursorRole.ResizeNorthwestSoutheast => "resize-nwse",
            CursorRole.ResizeNortheastSouthwest => "resize-nesw",
            CursorRole.Move => "move",
            CursorRole.Alternate => "alternate",
            CursorRole.Link => "link",
            CursorRole.Grab => "grab",
            CursorRole.Grabbing => "grabbing",
            CursorRole.ZoomIn => "zoom-in",
            CursorRole.ZoomOut => "zoom-out",
            CursorRole.Person => "person",
            CursorRole.Pin => "pin",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    /// <summary>Gets where cursor file <paramref name="name"/> is kept on the disk.</summary>
    public static string GetPath(string name)
    {
        return SystemPaths.CursorsFolder + "/" + name + ".cur";
    }

    /// <summary>Writes each built-in cursor to <see cref="SystemPaths.CursorsFolder"/> unless a file is there, so replaced cursors stay.</summary>
    public static void WriteMissingFiles()
    {
        // The roles in order, without Enum.GetValues: reflection is best avoided under Cosmos.
        for (CursorRole role = CursorRole.Arrow; role <= CursorRole.Pin; role++)
        {
            string name = GetFileName(role);
            string path = GetPath(name);
            if (!File.Exists(path) && ReadResource(name) is { } file)
            {
                SafeFile.WriteAllBytes(path, file);
            }
        }
    }

    private static CursorShape? Load(string name)
    {
        string path = GetPath(name);
        try
        {
            if (File.Exists(path))
            {
                return CursorShape.FromCursorFile(File.ReadAllBytes(path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SystemLog.Log($"Could not read the cursor {path}, using the built-in one: {ex.Message}", LogSeverity.Warning);
        }

        try
        {
            if (ReadResource(name) is { } file)
            {
                return CursorShape.FromCursorFile(file);
            }
        }
        catch (InvalidDataException ex)
        {
            SystemLog.Log($"The built-in cursor {name} is damaged: {ex.Message}", LogSeverity.Error);
        }

        return null;
    }

    private static byte[]? ReadResource(string name)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Cursors." + name + ".cur");
        if (stream is null)
        {
            SystemLog.Log($"The built-in cursor {name} is missing from the kernel.", LogSeverity.Error);
            return null;
        }

        byte[] file = new byte[stream.Length];
        stream.ReadExactly(file);
        return file;
    }
}
