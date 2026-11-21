using RemSox.Kernel.FileSystem;

namespace RemSox.Kernel.Accounts;

/// <summary>
/// Someone who can log in, or the built-in <see cref="System"/> account the kernel runs as. The
/// <see cref="Id"/> is what identifies an account: it never changes and is never given to
/// another account, so whatever records "who" (sessions now, file owners later) stores it.
/// </summary>
/// <remarks>Immutable: <see cref="AccountStore"/> replaces an account to change it.</remarks>
public sealed class Account(int id, string name, IReadOnlyList<string> groups)
{
    /// <summary>The group whose members manage accounts, and later pass permission checks.</summary>
    public const string AdminsGroup = "admins";

    /// <summary>The id of <see cref="System"/>.</summary>
    public const int SystemId = 0;

    /// <summary>The first id given to a user account; lower ones are kept for the system.</summary>
    public const int FirstUserId = 1000;

    /// <summary>
    /// The kernel's own account: the boot, the startup script and services run as it. It
    /// cannot log in and has no password.
    /// </summary>
    public static readonly Account System = new(SystemId, "system", [AdminsGroup]);

    public int Id { get; } = id;

    /// <summary>Gets the name typed to log in; unique ignoring case.</summary>
    public string Name { get; } = name;

    public IReadOnlyList<string> Groups { get; } = groups;

    public bool IsSystem => Id == SystemId;

    /// <summary>Gets whether the account is in <see cref="AdminsGroup"/> (the system always is).</summary>
    public bool IsAdmin => Groups.Contains(AdminsGroup);

    /// <summary>
    /// Gets the account's profile, <c>/users/&lt;name&gt;</c>: everything that belongs to it, its
    /// own files (<see cref="HomeFolder"/>) apart from what the system keeps for it
    /// (<see cref="ConfigFolder"/>, <see cref="ProgramsFolder"/>). Null for the system, which has none.
    /// </summary>
    public string? ProfileFolder => IsSystem ? null : SystemPaths.UsersFolder + "/" + Name;

    /// <summary>
    /// Gets the account's home folder, <c>/users/&lt;name&gt;/home</c>: its own files, where its
    /// shells start and <c>cd</c> alone goes. The system's is the root folder.
    /// </summary>
    public string HomeFolder => ProfileFolder is { } profile ? profile + "/home" : FileSystemManager.MountPoint;

    /// <summary>Gets the folder of what the system keeps for the account (its startup script; later its settings), or null for the system.</summary>
    public string? ConfigFolder => ProfileFolder is { } profile ? profile + "/config" : null;

    /// <summary>Gets the account's own programs folder, searched before the <c>path</c> folders, or null for the system.</summary>
    public string? ProgramsFolder => ProfileFolder is { } profile ? profile + "/programs" : null;

    public override string ToString()
    {
        return Name;
    }
}
