using RemSox.Kernel.FileSystem;

namespace RemSox.Kernel.Accounts;

/// <summary>
/// Someone who can log in, or the built-in <see cref="System"/> account the kernel runs as. The
/// <see cref="Id"/> is what identifies an account: it never changes and is never given to
/// another account, so whatever records "who" (sessions now, file owners later) stores it.
/// User accounts count up from 0; built-in accounts count down from -1 (<see cref="System"/>),
/// as the system's processes do, so the two can never meet.
/// </summary>
/// <remarks>Immutable: <see cref="AccountStore"/> replaces an account to change it.</remarks>
public sealed class Account(int id, string name, IReadOnlyList<int> groupIds)
{
    /// <summary>The id of <see cref="System"/>, the first built-in account.</summary>
    public const int SystemId = -1;

    /// <summary>The first id given to a user account; negative ones belong to built-in accounts.</summary>
    public const int FirstUserId = 0;

    /// <summary>
    /// The kernel's own account: the boot and services run as it. It cannot log in and has no
    /// password.
    /// </summary>
    public static readonly Account System = new(SystemId, "system", [Group.AdminsId]);

    public int Id { get; } = id;

    /// <summary>Gets the name typed to log in; unique ignoring case.</summary>
    public string Name { get; } = name;

    /// <summary>Gets the ids of the account's groups (see <see cref="GroupStore"/>).</summary>
    public IReadOnlyList<int> GroupIds { get; } = groupIds;

    /// <summary>Gets whether this is <see cref="System"/>, the kernel's own account.</summary>
    public bool IsSystem => Id == SystemId;

    /// <summary>Gets whether the account is built in (a negative id) rather than one someone created.</summary>
    public bool IsBuiltIn => Id < FirstUserId;

    /// <summary>Gets whether the account is in <see cref="Group.Admins"/> (the system always is).</summary>
    public bool IsAdmin => IsInGroup(Group.AdminsId);

    /// <summary>Returns whether the account is in the group with <paramref name="groupId"/>.</summary>
    public bool IsInGroup(int groupId)
    {
        return GroupIds.Contains(groupId);
    }

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

    /// <summary>
    /// Gets the account's own start menu folder, whose shortcuts its start menu shows next to the
    /// shared ones, or null for the system.
    /// </summary>
    public string? StartMenuFolder => ConfigFolder is { } config ? config + "/startmenu" : null;

    /// <summary>
    /// Gets the account's own cursors folder: a <c>.cur</c> file there named like one of the
    /// system's (<c>arrow.cur</c>, <c>text.cur</c>, ...) replaces that cursor for the account,
    /// as its start menu folder adds to the shared one. Null for the system.
    /// </summary>
    public string? CursorsFolder => ConfigFolder is { } config ? config + "/cursors" : null;

    /// <summary>Gets the account's own programs folder, searched before the <c>path</c> folders, or null for the system.</summary>
    public string? ProgramsFolder => ProfileFolder is { } profile ? profile + "/programs" : null;

    public override string ToString()
    {
        return Name;
    }
}
