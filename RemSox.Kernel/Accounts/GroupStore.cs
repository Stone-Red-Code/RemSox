using System.Globalization;

using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;
using RemSox.Kernel.Processing;

namespace RemSox.Kernel.Accounts;

/// <summary>
/// The groups, kept in <see cref="SystemPaths.GroupsFile"/>: one <c>id:name</c> line per group an
/// administrator made, like Linux's <c>/etc/group</c> without the members (each account lists
/// its own groups). Built-in groups (<see cref="Group.BuiltIn"/>) are not in the file.
/// </summary>
public static class GroupStore
{
    private const string HighestIdPrefix = "#highest id:";

    private static readonly List<Group> groups = [];

    // The highest id ever given, so a deleted group's id is never given again, kept in the
    // file's "#highest id:" line like the accounts' one.
    private static int highestId = Group.FirstUserId - 1;

    /// <summary>Gets every group: the built-in ones, then the ones administrators made, in the order they were made.</summary>
    public static IEnumerable<Group> All => Group.BuiltIn.Concat(groups);

    /// <summary>Reads the file; called by <see cref="AccountStore.Load"/>, before the accounts that refer to the groups.</summary>
    public static void Load()
    {
        groups.Clear();
        highestId = Group.FirstUserId - 1;

        if (!FileSystemManager.IsMounted || !File.Exists(SystemPaths.GroupsFile))
        {
            return;
        }

        try
        {
            foreach (string line in File.ReadAllLines(SystemPaths.GroupsFile))
            {
                ReadLine(line);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SystemLog.Log("Could not read the groups: " + ex.Message, LogSeverity.Error);
        }
    }

    /// <summary>Returns the group called <paramref name="name"/>, ignoring case, or null.</summary>
    public static Group? Find(string name)
    {
        return All.FirstOrDefault(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns the group with <paramref name="id"/>, or null.</summary>
    public static Group? Find(int id)
    {
        return All.FirstOrDefault(group => group.Id == id);
    }

    /// <summary>Returns the accounts in <paramref name="group"/>.</summary>
    public static IEnumerable<Account> GetMembers(Group group)
    {
        return AccountStore.All.Where(account => account.IsInGroup(group.Id));
    }

    /// <summary>Makes a group; it starts without members.</summary>
    /// <returns>The group, or why it could not be made.</returns>
    public static (Group? Group, string? Error) Create(string name)
    {
        MainLoop.AssertMainThread(nameof(Create));

        if (!FileSystemManager.IsMounted)
        {
            return (null, FileSystemManager.NoDiskMessage);
        }

        name = name.ToLowerInvariant();
        if (!AccountStore.IsWellFormedName(name))
        {
            return (null, $"A group name is 1-{AccountStore.MaxNameLength} characters a-z, 0-9, _ or -, starting with a letter.");
        }

        if (Find(name) is not null)
        {
            return (null, $"There is already a group called {name}.");
        }

        Group group = new(highestId + 1, name);
        groups.Add(group);
        if (Save() is { } error)
        {
            _ = groups.Remove(group);
            return (null, error);
        }

        highestId = group.Id;
        SystemLog.Log($"Created group {group.Name} (id {group.Id}).", LogSeverity.Info);
        return (group, null);
    }

    /// <summary>
    /// Deletes a group and takes every account out of it. Built-in groups cannot be deleted,
    /// and the id is never given again.
    /// </summary>
    /// <returns>Null, or why it could not be deleted.</returns>
    public static string? Delete(Group group)
    {
        MainLoop.AssertMainThread(nameof(Delete));

        if (group.IsBuiltIn)
        {
            return $"{group.Name} is built in and cannot be deleted.";
        }

        int index = groups.IndexOf(group);
        if (index < 0)
        {
            return $"There is no group called {group.Name}.";
        }

        // The accounts first: a group still listed with no members is harmless, an account
        // listing a group that is gone is ignored with a warning at the next boot.
        if (AccountStore.RemoveFromGroup(group.Id) is { } error)
        {
            return error;
        }

        groups.RemoveAt(index);
        if (Save() is { } saveError)
        {
            groups.Insert(index, group);
            return saveError;
        }

        SystemLog.Log($"Deleted group {group.Name} (id {group.Id}).", LogSeverity.Info);
        return null;
    }

    private static string? Save()
    {
        try
        {
            SafeFile.WriteAllLines(SystemPaths.GroupsFile, [
                "# RemSox groups: id:name, one per group made with 'group add'. Built-in groups such as",
                "# admins are not listed; each account lists the ids of its groups.",
                $"{HighestIdPrefix}{Math.Max(highestId, groups.Select(g => g.Id).DefaultIfEmpty(Group.FirstUserId - 1).Max()).ToString(CultureInfo.InvariantCulture)}",
                .. groups.Select(g => $"{g.Id.ToString(CultureInfo.InvariantCulture)}:{g.Name}"),
            ]);

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SystemLog.Log("Could not save the groups: " + ex.Message, LogSeverity.Error);
            return "Could not save the groups: " + ex.Message;
        }
    }

    private static void ReadLine(string line)
    {
        if (line.StartsWith(HighestIdPrefix, StringComparison.Ordinal))
        {
            if (int.TryParse(line[HighestIdPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int highest))
            {
                highestId = Math.Max(highestId, highest);
            }

            return;
        }

        if (line.Length == 0 || line[0] == '#')
        {
            return;
        }

        string[] parts = line.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int id)
            || !AccountStore.IsWellFormedName(parts[1])
            || Find(parts[1]) is not null
            || Find(id) is not null)
        {
            SystemLog.Log($"{SystemPaths.GroupsFile}: ignored the line \"{line}\"", LogSeverity.Warning);
            return;
        }

        groups.Add(new Group(id, parts[1].ToLowerInvariant()));
        highestId = Math.Max(highestId, id);
    }
}
