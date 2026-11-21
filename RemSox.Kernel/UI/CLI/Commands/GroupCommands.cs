using System.Globalization;

using RemSox.Kernel.Accounts;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class GroupsCommand : ICommand
{
    public string Name => "groups";

    public string Description => "List the groups and their members, or the groups of one account";

    public string Usage => "groups [account]";

    public int MaxArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        if (context.Args.Count == 1)
        {
            if (AccountStore.Find(context.Args[0]) is not { } account)
            {
                return Task.FromResult(context.Fail($"There is no account called {context.Args[0]}."));
            }

            context.Console.WriteLine($"{account.Name}: {AccountCommandHelpers.FormatGroups(account)}");
            return Task.FromResult(CommandResult.Success);
        }

        foreach (Group group in GroupStore.All)
        {
            string members = string.Join(", ", GroupStore.GetMembers(group).Select(a => a.Name));
            context.Console.WriteLine($"{group.Id.ToString(CultureInfo.InvariantCulture),5}  {group.Name,-16} {(members.Length > 0 ? members : "-")}");
        }

        return Task.FromResult(CommandResult.Success);
    }
}

public sealed class AddGroupCommand : ICommand
{
    public string Name => "groupadd";

    public string Description => "Make a group (administrators only)";

    public string Usage => "groupadd <name>";

    public int MinArguments => 1;

    public int MaxArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        if (AccountCommandHelpers.RequireAdmin(context) is { } denied)
        {
            return Task.FromResult(denied);
        }

        (Group? group, string? error) = GroupStore.Create(context.Args[0]);
        if (group is null)
        {
            return Task.FromResult(context.Fail(error!));
        }

        context.Console.WriteLine($"Created group {group.Name} (id {group.Id.ToString(CultureInfo.InvariantCulture)}). Add accounts with: usermod <account> --add-group {group.Name}");
        return Task.FromResult(CommandResult.Success);
    }
}

public sealed class DeleteGroupCommand : ICommand
{
    public string Name => "groupdel";

    public string Description => "Delete a group and take its members out of it (administrators only)";

    public string Usage => "groupdel <name>";

    public int MinArguments => 1;

    public int MaxArguments => 1;

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        if (AccountCommandHelpers.RequireAdmin(context) is { } denied)
        {
            return denied;
        }

        if (GroupStore.Find(context.Args[0]) is not { } group)
        {
            return context.Fail($"There is no group called {context.Args[0]}.");
        }

        if (group.IsBuiltIn)
        {
            return context.Fail($"{group.Name} is built in and cannot be deleted.");
        }

        int members = GroupStore.GetMembers(group).Count();
        if (members > 0 && !await context.ConfirmAsync($"{group.Name} has {members} member(s). Delete it?"))
        {
            return CommandResult.Cancelled;
        }

        if (GroupStore.Delete(group) is { } error)
        {
            return context.Fail(error);
        }

        context.Console.WriteLine($"Deleted group {group.Name}.");
        return CommandResult.Success;
    }
}

/// <summary>Changes an account's groups. Like on Linux, a change applies from the account's next login.</summary>
public sealed class ModifyUserCommand : ICommand
{
    private const string AddOption = "--add-group";
    private const string RemoveOption = "--remove-group";

    public string Name => "usermod";

    public string Description => "Add an account to a group or take it out (administrators only; applies at its next login)";

    public string Usage => $"usermod <account> {AddOption}|{RemoveOption} <group>";

    public int MinArguments => 3;

    public int MaxArguments => 3;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        if (AccountCommandHelpers.RequireAdmin(context) is { } denied)
        {
            return Task.FromResult(denied);
        }

        bool add = context.Args[1] == AddOption;
        if (!add && context.Args[1] != RemoveOption)
        {
            return Task.FromResult(context.UsageError());
        }

        if (AccountStore.Find(context.Args[0]) is not { } account)
        {
            return Task.FromResult(context.Fail($"There is no account called {context.Args[0]}."));
        }

        if (GroupStore.Find(context.Args[2]) is not { } group)
        {
            return Task.FromResult(context.Fail($"There is no group called {context.Args[2]}."));
        }

        if (account.IsInGroup(group.Id) == add)
        {
            return Task.FromResult(context.Fail($"{account.Name} is {(add ? "already" : "not")} in {group.Name}."));
        }

        IReadOnlyList<int> groupIds = add ? [.. account.GroupIds, group.Id] : [.. account.GroupIds.Where(id => id != group.Id)];
        (Account? changed, string? error) = AccountStore.SetGroups(account, groupIds);
        if (changed is null)
        {
            return Task.FromResult(context.Fail(error!));
        }

        context.Console.WriteLine($"{(add ? "Added" : "Removed")} {account.Name} {(add ? "to" : "from")} {group.Name}; it applies from {account.Name}'s next login.");
        return Task.FromResult(CommandResult.Success);
    }
}
