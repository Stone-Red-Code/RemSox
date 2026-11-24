using RemSox.Kernel.Accounts;
using RemSox.Kernel.FileSystem;

namespace RemSox.Kernel.Security;

/// <summary>
/// The file operations commands and programs do on a caller's behalf, each checked with
/// <see cref="Permissions"/> before it touches the disk: the one way user-given paths reach the
/// file system. Paths are absolute (resolved against the caller's working directory first); the
/// checked, canonical path is the one used, so nothing changes between the check and the use.
/// </summary>
/// <remarks>
/// A refusal throws <see cref="UnauthorizedAccessException"/> ("Permission denied: path"); a path
/// that exists only under another spelling throws <see cref="FileNotFoundException"/>. Callers
/// already catch both as file errors. The system's own code (accounts, settings, logs) uses
/// <see cref="File"/> directly and is not checked.
/// </remarks>
public static class UserFiles
{
    /// <summary>Returns the canonical path after checking <paramref name="access"/> on it.</summary>
    public static string Authorize(Caller caller, string path, Access access)
    {
        string canonical = Canonical(path);
        Demand(caller, canonical, access);
        return canonical;
    }

    /// <summary>
    /// Returns the canonical path of something about to be created or changed: an existing item
    /// needs <see cref="Access.Write"/> on itself, a new one on the folder it goes in.
    /// </summary>
    public static string AuthorizeWrite(Caller caller, string path)
    {
        string canonical = Canonical(path);
        Demand(caller, Exists(canonical) ? canonical : Parent(canonical), Access.Write);
        return canonical;
    }

    /// <summary>Returns whether the file or folder exists, as far as the caller may see: false when they may not read its folder.</summary>
    public static bool Exists(Caller caller, string path)
    {
        if (Permissions.Canonicalize(path, "/") is not { } canonical)
        {
            return false;
        }

        return Permissions.Can(caller, Parent(canonical), Access.Read) && Exists(canonical);
    }

    /// <summary>Returns whether <paramref name="path"/> is a folder the caller may go into.</summary>
    public static bool IsReadableFolder(Caller caller, string path)
    {
        return Permissions.Canonicalize(path, "/") is { } canonical
            && Directory.Exists(canonical)
            && Permissions.Can(caller, canonical, Access.Read);
    }

    /// <summary>Lists a folder: its subfolders and files.</summary>
    public static (DirectoryInfo[] Folders, FileInfo[] Files) List(Caller caller, string path)
    {
        DirectoryInfo folder = new(Authorize(caller, path, Access.Read));
        if (!folder.Exists)
        {
            throw new DirectoryNotFoundException("No such directory: " + path);
        }

        return (folder.GetDirectories(), folder.GetFiles());
    }

    /// <summary>Returns whether the folder has anything in it.</summary>
    public static bool HasEntries(Caller caller, string path)
    {
        return Directory.EnumerateFileSystemEntries(Authorize(caller, path, Access.Read)).Any();
    }

    public static string[] ReadAllLines(Caller caller, string path)
    {
        return File.ReadAllLines(Authorize(caller, path, Access.Read));
    }

    public static byte[] ReadAllBytes(Caller caller, string path)
    {
        return File.ReadAllBytes(Authorize(caller, path, Access.Read));
    }

    public static void WriteAllText(Caller caller, string path, string text)
    {
        File.WriteAllText(AuthorizeWrite(caller, path), text);
    }

    public static void AppendAllText(Caller caller, string path, string text)
    {
        File.AppendAllText(AuthorizeWrite(caller, path), text);
    }

    /// <summary>Saves lines as <see cref="SafeFile.WriteAllLines"/> does, so a crash cannot leave half a file.</summary>
    public static void SafeWriteAllLines(Caller caller, string path, IEnumerable<string> lines)
    {
        string canonical = AuthorizeWrite(caller, path);

        // SafeFile writes a .new file beside it first, which needs the folder too.
        Demand(caller, Parent(canonical), Access.Write);
        SafeFile.WriteAllLines(canonical, lines);
    }

    /// <summary>Creates an empty file unless one exists.</summary>
    public static void CreateFile(Caller caller, string path)
    {
        string canonical = AuthorizeWrite(caller, path);
        if (!File.Exists(canonical))
        {
            File.WriteAllBytes(canonical, []);
        }
    }

    /// <summary>Creates a folder and any missing folders above it; each needs <see cref="Access.Write"/> on the one it goes in.</summary>
    public static void CreateDirectory(Caller caller, string path)
    {
        string canonical = Canonical(path);
        string current = FileSystemManager.MountPoint;

        foreach (string part in canonical.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string next = current.TrimEnd('/') + "/" + part;
            if (!Directory.Exists(next))
            {
                Demand(caller, current, Access.Write);
                _ = Directory.CreateDirectory(next);
            }

            current = next;
        }
    }

    /// <summary>
    /// Deletes a file, or a folder (with everything in it when <paramref name="recursive"/>). Needs
    /// <see cref="Access.Write"/> on the folder it is in and, for a folder, on every folder inside,
    /// all checked before anything is deleted, so a refusal deep inside deletes nothing.
    /// </summary>
    public static void Delete(Caller caller, string path, bool recursive)
    {
        string canonical = Canonical(path);
        Demand(caller, Parent(canonical), Access.Write);

        if (File.Exists(canonical))
        {
            Demand(caller, canonical, Access.Write);
            File.Delete(canonical);
            return;
        }

        if (!Directory.Exists(canonical))
        {
            throw new FileNotFoundException("No such file or directory: " + path);
        }

        DemandTree(caller, canonical, Access.Write);
        Directory.Delete(canonical, recursive);
    }

    /// <summary>Copies a file: <see cref="Access.Read"/> on it, <see cref="Access.Write"/> where the copy goes.</summary>
    public static void CopyFile(Caller caller, string source, string destination, bool overwrite)
    {
        string from = Authorize(caller, source, Access.Read);
        File.Copy(from, AuthorizeWrite(caller, destination), overwrite);
    }

    /// <summary>Moves or renames a file or folder: <see cref="Access.Write"/> on it and on both folders.</summary>
    public static void Move(Caller caller, string source, string destination, bool overwrite)
    {
        string from = Canonical(source);
        Demand(caller, Parent(from), Access.Write);
        Demand(caller, from, Access.Write);

        string to = Canonical(destination);
        Demand(caller, Parent(to), Access.Write);

        if (Directory.Exists(from))
        {
            DemandTree(caller, from, Access.Write);
            Directory.Move(from, to);
        }
        else
        {
            File.Move(from, to, overwrite);
        }
    }

    private static string Canonical(string path)
    {
        return Permissions.Canonicalize(path, "/") ?? throw new FileNotFoundException("No such file or directory: " + path);
    }

    private static void Demand(Caller caller, string canonicalPath, Access access)
    {
        if (!Permissions.Can(caller, canonicalPath, access))
        {
            throw new UnauthorizedAccessException("Permission denied: " + canonicalPath);
        }
    }

    /// <summary>Demands <paramref name="access"/> on a folder and on every folder and file inside it.</summary>
    private static void DemandTree(Caller caller, string folder, Access access)
    {
        Demand(caller, folder, access);

        foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
        {
            if (Directory.Exists(entry))
            {
                DemandTree(caller, entry, access);
            }
            else
            {
                Demand(caller, entry, access);
            }
        }
    }

    private static bool Exists(string canonicalPath)
    {
        return File.Exists(canonicalPath) || Directory.Exists(canonicalPath);
    }

    private static string Parent(string canonicalPath)
    {
        return Path.GetDirectoryName(canonicalPath) ?? FileSystemManager.MountPoint;
    }
}
