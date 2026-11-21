using RemSox.Kernel.Accounts;
using RemSox.Kernel.FileSystem;

namespace RemSox.Kernel.Security;

/// <summary>What a caller may do with a path.</summary>
[Flags]
public enum Access
{
    None = 0,

    /// <summary>A folder: list it and go into it. A file: read it, run it.</summary>
    Read = 1,

    /// <summary>A folder: create, rename and delete what is in it. A file: change it.</summary>
    Write = 2,

    ReadWrite = Read | Write,
}

/// <summary>
/// Decides who may read and write which path (see <c>docs/design/permissions.md</c>). Rights
/// belong to folders and are inherited by everything below them, since FAT has nowhere to keep
/// rights per file: built-in rules follow the disk layout, and the rules file adds to them.
/// </summary>
/// <remarks>
/// The system may do everything; an elevated administrator everything but read the secrets,
/// which only the system may. Every path is brought to its one exact spelling first
/// (<see cref="Canonicalize"/>): Cosmos's FAT driver ignores case and also accepts FAT's 8.3
/// short names, which would otherwise get past a rule written for the long name.
/// </remarks>
public static class Permissions
{
    /// <summary>
    /// Returns the one spelling of <paramref name="path"/> every rule is written in: absolute, with
    /// <c>.</c>, <c>..</c> and repeated <c>/</c> removed, and each existing part spelt as the
    /// directory stores it. Null when a part exists only under another spelling (an 8.3 short
    /// name such as <c>SETTIN~1.CON</c>), which is refused rather than checked under the wrong name.
    /// Parts that do not exist yet (a file about to be created) are kept as given.
    /// </summary>
    public static string? Canonicalize(string path, string workingDirectory)
    {
        string full = Path.GetFullPath(path, workingDirectory);
        if (!FileSystemManager.IsMounted)
        {
            return full;
        }

        string current = FileSystemManager.MountPoint;
        bool exists = true;

        foreach (string part in full.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string next = current.TrimEnd('/') + "/" + part;

            if (exists)
            {
                string? stored = Directory.EnumerateFileSystemEntries(current)
                    .Select(Path.GetFileName)
                    .FirstOrDefault(name => string.Equals(name, part, StringComparison.OrdinalIgnoreCase));

                if (stored is not null)
                {
                    next = current.TrimEnd('/') + "/" + stored;
                }
                else if (File.Exists(next) || Directory.Exists(next))
                {
                    // Found, but under no stored name: an 8.3 alias of something else.
                    return null;
                }
                else
                {
                    exists = false;
                }
            }

            current = next;
        }

        return current;
    }

    /// <summary>Returns what <paramref name="caller"/> may do with <paramref name="canonicalPath"/> (from <see cref="Canonicalize"/>).</summary>
    public static Access GetAccess(Caller caller, string canonicalPath)
    {
        return GetAccess(caller, canonicalPath, withRules: true);
    }

    /// <summary>Returns what the built-in rules alone let <paramref name="caller"/> do, without the rules file.</summary>
    public static Access GetBuiltInAccess(Caller caller, string canonicalPath)
    {
        return GetAccess(caller, canonicalPath, withRules: false);
    }

    private static Access GetAccess(Caller caller, string canonicalPath, bool withRules)
    {
        if (caller.IsSystem)
        {
            return Access.ReadWrite;
        }

        // Closed to everyone but the system, whatever any rule says.
        if (IsUnder(canonicalPath, SystemPaths.SecretsFolder))
        {
            return Access.None;
        }

        if (caller.HasAdminRights)
        {
            return Access.ReadWrite;
        }

        Access builtIn = BuiltInAccess(caller.Account, canonicalPath);
        return withRules ? builtIn | PermissionRules.GetAccess(caller.Account, canonicalPath) : builtIn;
    }

    /// <summary>Returns whether <paramref name="caller"/> may do <paramref name="access"/> with <paramref name="canonicalPath"/>.</summary>
    public static bool Can(Caller caller, string canonicalPath, Access access)
    {
        return (GetAccess(caller, canonicalPath) & access) == access;
    }

    /// <summary>Returns whether <paramref name="path"/> is <paramref name="folder"/> or inside it (both canonical).</summary>
    public static bool IsUnder(string path, string folder)
    {
        return string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(folder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The rules that come from the disk layout, for an account without admin rights: the most
    /// specific folder decides.
    /// </summary>
    private static Access BuiltInAccess(Account account, string path)
    {
        if (IsUnder(path, SystemPaths.UsersFolder) && !string.Equals(path, SystemPaths.UsersFolder, StringComparison.OrdinalIgnoreCase))
        {
            // /users/<name> and everything in it: its owner's.
            string name = path[(SystemPaths.UsersFolder.Length + 1)..].Split('/')[0];
            return string.Equals(name, account.Name, StringComparison.OrdinalIgnoreCase) && !account.IsBuiltIn
                ? Access.ReadWrite
                : Access.None;
        }

        if (IsUnder(path, SystemPaths.TempFolder))
        {
            return Access.ReadWrite;
        }

        if (IsUnder(path, SystemPaths.LogsFolder))
        {
            return Access.None;
        }

        // /, /system (configuration, start menu), /programs, /users and any folder an
        // administrator made at the top: everyone reads, only administrators change.
        return Access.Read;
    }
}
