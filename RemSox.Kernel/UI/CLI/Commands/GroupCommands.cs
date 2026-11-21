using System.Globalization;

using RemSox.Kernel.Accounts;

namespace RemSox.Kernel.UI.CLI.Commands;

/// <summary>
/// Everything about groups, as one command with an action: list them, add and remove one, and
/// change who is in it. A change of membership applies from the account's next login.
/// </summary>
public sealed class GroupCommand : ICommand
{
    public string Name => "group";

    public string Description => "List the groups, add or remove one, and change who is in it";

    public string Usage => "group list | group add <name> | group remove <name> | group add-member <group> <account> | group remove-member <group> <account>";

    public Task<int> ExecuteAsync(CommandContext context)
    {
        IReadOnlyList<string> args = context.Args;
        string action = args.Count > 0 ? args[0].ToLowerInvariant() : string.Empty;

        return (action, args.Count) switch
        {
            ("list", 1) => Task.FromResult(List(context)),
            ("add", 2) => Task.FromResult(Add(context, args[1])),
            ("remove", 2) => RemoveAsync(context, args[1]),
            ("add-member", 3) => Task.FromResult(ChangeMember(context, args[1], args[2], add: true)),
            ("remove-member", 3) => Task.FromResult(ChangeMember(context, args[1], args[2], add: false)),
            _ => Task.FromResult(context.UsageError()),
        };
    }

    private static int List(CommandContext context)
    {
        foreach (Group group in GroupStore.All)
        {
            string members = string.Join(", ", GroupStore.GetMembers(group).Select(a => a.Name));
            context.Console.WriteLine($"{group.Id.ToString(CultureInfo.InvariantCulture),5}  {group.Name,-16} {(members.Length > 0 ? members : "-")}");
        }

        return CommandResult.Success;
    }

    private static int Add(CommandContext context, string name)
    {
        if (AccountCommandHelpers.RequireAdmin(context, "add groups") is { } denied)
        {
            return denied;
        }

        (Group? group, string? error) = GroupStore.Create(name);
        if (group is null)
        {
            return context.Fail(error!);
        }

        context.Console.WriteLine($"Created group {group.Name} (id {group.Id.ToString(CultureInfo.InvariantCulture)}). Add accounts with: group add-member {group.Name} <account>");
        return CommandResult.Success;
    }

    /// <summary>Deletes a group, taking its members out of it.</summary>
    private static async Task<int> RemoveAsync(CommandContext context, string name)
    {
        if (AccountCommandHelpers.RequireAdmin(context, "remove groups") is { } denied)
        {
            return denied;
        }

        if (FindGroup(context, name) is not { } group)
        {
            return CommandResult.Failure;
        }

        if (group.IsBuiltIn)
        {
            return context.Fail($"{group.Name} is built in and cannot be removed.");
        }

        int members = GroupStore.GetMembers(group).Count();
        if (members > 0 && !await context.ConfirmAsync($"{group.Name} has {members} member(s). Remove it?"))
        {
            return CommandResult.Cancelled;
        }

        if (GroupStore.Delete(group) is { } error)
        {
            return context.Fail(error);
        }

        context.Console.WriteLine($"Removed group {group.Name}.");
        return CommandResult.Success;
    }

    /// <summary>Puts an account in a group or takes it out; it applies from the account's next login.</summary>
    private static int ChangeMember(CommandContext context, string groupName, string accountName, bool add)
    {
        if (AccountCommandHelpers.RequireAdmin(context, "change who is in a group") is { } denied)
        {
            return denied;
        }

        if (FindGroup(context, groupName) is not { } group || AccountCommandHelpers.FindAccount(context, accountName) is not { } account)
        {
            return CommandResult.Failure;
        }

        if (account.IsInGroup(group.Id) == add)
        {
            return context.Fail($"{account.Name} is {(add ? "already" : "not")} in {group.Name}.");
        }

        IReadOnlyList<int> groupIds = add ? [.. account.GroupIds, group.Id] : [.. account.GroupIds.Where(id => id != group.Id)];
        (Account? changed, string? error) = AccountStore.SetGroups(account, groupIds);
        if (changed is null)
        {
            return context.Fail(error!);
        }

        context.Console.WriteLine($"{(add ? "Added" : "Removed")} {account.Name} {(add ? "to" : "from")} {group.Name}; it applies from {account.Name}'s next login.");
        return CommandResult.Success;
    }

    private static Group? FindGroup(CommandContext context, string name)
    {
        Group? group = GroupStore.Find(name);
        if (group is null)
        {
            context.Console.WriteError($"There is no group called {name}.");
        }

        return group;
    }
}
