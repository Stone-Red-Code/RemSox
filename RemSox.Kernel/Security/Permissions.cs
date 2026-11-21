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
/// rights per file: built-in rules (<see cref="BuiltInRules"/>) follow the disk layout, and the
/// rules file (<see cref="PermissionRules"/>) adds to them. Both are the same kind of rule, so
/// <c>perm show</c> lists them alike.
/// </summary>
/// <remarks>
/// The system may do everything (the kernel is trusted). Every path is brought to its one exact spelling first
/// (<see cref="Canonicalize"/>): Cosmos's FAT driver ignores case and also accepts FAT's 8.3
/// short names, which would otherwise get past a rule written for the long name.
/// </remarks>
public static class Permissions
{
    /// <summary>The path of a built-in rule for every profile folder, <c>/users/&lt;name&gt;</c>.</summary>
    private const string EachProfile = SystemPaths.UsersFolder + "/*";

    private static readonly RuleSubject Admins = new(RuleSubjectKind.Group, Group.AdminsId);

    /// <summary>
    /// The rules that come from the disk layout. As in the rules file, the most specific path
    /// decides, so each path lists everyone it lets in. Administrators (<see cref="Group.Admins"/>,
    /// which counts only with admin rights) may do everything but read the secrets, which only the
    /// system may; logs are the system's and administrators'; profiles their owner's.
    /// </summary>
    public static readonly IReadOnlyList<PermissionRule> BuiltInRules =
    [
        // /, /system (configuration, start menu), /programs, /users and any folder an
        // administrator made at the top: everyone reads, only administrators change.
        new("/", RuleSubject.Everyone, Access.Read),
        new("/", Admins, Access.ReadWrite),
        new(SystemPaths.TempFolder, RuleSubject.Everyone, Access.ReadWrite),
        new(SystemPaths.TempFolder, Admins, Access.ReadWrite),
        new(SystemPaths.LogsFolder, Admins, Access.ReadWrite),
        new(SystemPaths.SecretsFolder, new RuleSubject(RuleSubjectKind.Account, Account.SystemId), Access.ReadWrite),
        new(EachProfile, RuleSubject.Owner, Access.ReadWrite),
        new(EachProfile, Admins, Access.ReadWrite),
    ];

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
        if (caller.IsSystem)
        {
            return Access.ReadWrite;
        }

        Access rights = Access.None;
        foreach (PermissionRule rule in GetDecidingRules(canonicalPath))
        {
            if (rule.Subject.Includes(caller, canonicalPath))
            {
                rights |= rule.Rights;
            }
        }

        return rights;
    }

    /// <summary>
    /// Returns who may do what with <paramref name="canonicalPath"/>, for people to read: each
    /// subject the deciding built-in and file rules name, with everything it gets (an account or
    /// group also gets what everyone does). A profile's owner is named as that account.
    /// </summary>
    public static IReadOnlyList<(RuleSubject Subject, Access Rights)> GetOverview(string canonicalPath)
    {
        List<(RuleSubject Subject, Access Rights)> overview = [];
        foreach (PermissionRule rule in GetDecidingRules(canonicalPath))
        {
            RuleSubject subject = rule.Subject;
            if (subject.Kind == RuleSubjectKind.Owner)
            {
                if (AccountStore.Find(GetProfileOwner(canonicalPath) ?? string.Empty) is not { IsBuiltIn: false } owner)
                {
                    continue;
                }

                subject = new RuleSubject(RuleSubjectKind.Account, owner.Id);
            }

            int index = overview.FindIndex(entry => entry.Subject == subject);
            if (index >= 0)
            {
                overview[index] = (subject, overview[index].Rights | rule.Rights);
            }
            else
            {
                overview.Add((subject, rule.Rights));
            }
        }

        // Everyone first; the others also get what everyone does.
        int everyone = overview.FindIndex(entry => entry.Subject == RuleSubject.Everyone);
        if (everyone >= 0)
        {
            (RuleSubject Subject, Access Rights) entry = overview[everyone];
            overview.RemoveAt(everyone);
            for (int i = 0; i < overview.Count; i++)
            {
                overview[i] = (overview[i].Subject, overview[i].Rights | entry.Rights);
            }

            overview.Insert(0, entry);
        }

        return overview;
    }

    /// <summary>The built-in rules and the rules file's that decide for <paramref name="canonicalPath"/>.</summary>
    private static IEnumerable<PermissionRule> GetDecidingRules(string canonicalPath)
    {
        // A profile's own path stands for every profile's: /users/bob matches /users/*.
        string? owner = GetProfileOwner(canonicalPath);
        string builtInPath = owner is null ? canonicalPath : EachProfile + canonicalPath[(SystemPaths.UsersFolder.Length + 1 + owner.Length)..];

        return PermissionRules.GetDecidingRules(BuiltInRules, builtInPath).Concat(PermissionRules.GetDecidingRules(canonicalPath));
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

    /// <summary>Returns the name of the account whose profile (<c>/users/&lt;name&gt;</c>) <paramref name="path"/> is in, or null.</summary>
    public static string? GetProfileOwner(string path)
    {
        return IsUnder(path, SystemPaths.UsersFolder) && !string.Equals(path, SystemPaths.UsersFolder, StringComparison.OrdinalIgnoreCase)
            ? path[(SystemPaths.UsersFolder.Length + 1)..].Split('/')[0]
            : null;
    }
}
