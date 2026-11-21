namespace RemSox.Kernel.Accounts;

/// <summary>
/// A named set of accounts, which permissions will be granted to. Like an account, a group is
/// identified by its <see cref="Id"/>, which never changes and is never given to another group:
/// accounts record the ids of their groups, so renaming a group costs nothing. Groups made by
/// administrators count up from 0; built-in groups count down from -1 (<see cref="Admins"/>).
/// </summary>
public sealed class Group(int id, string name)
{
    /// <summary>The id of <see cref="Admins"/>.</summary>
    public const int AdminsId = -1;

    /// <summary>The first id given to a group someone makes; negative ones belong to built-in groups.</summary>
    public const int FirstUserId = 0;

    /// <summary>
    /// The administrators: they manage accounts, groups and services, and will pass the
    /// permission checks that come later. The system is always one, and the last member cannot
    /// leave it.
    /// </summary>
    public static readonly Group Admins = new(AdminsId, "admins");

    /// <summary>The groups every system has; they cannot be deleted.</summary>
    public static readonly IReadOnlyList<Group> BuiltIn = [Admins];

    public int Id { get; } = id;

    /// <summary>Gets the group's name; unique among groups, ignoring case.</summary>
    public string Name { get; } = name;

    /// <summary>Gets whether the group is built in (a negative id) rather than one someone made.</summary>
    public bool IsBuiltIn => Id < FirstUserId;

    public override string ToString()
    {
        return Name;
    }
}
