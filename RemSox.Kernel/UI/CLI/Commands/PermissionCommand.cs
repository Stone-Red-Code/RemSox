using RemSox.Kernel.Accounts;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Security;

namespace RemSox.Kernel.UI.CLI.Commands;

/// <summary>
/// Shows who may do what with a path and why, and adds or removes rules (see
/// <see cref="PermissionRules"/>): anywhere for an administrator with <c>sudo</c>, inside their
/// own profile for its owner, who can share a folder of theirs this way.
/// </summary>
public sealed class PermissionCommand : ICommand
{
    public string Name => "perm";

    public string Description => "Show who may read or write a path, or grant and revoke rights on it";

    public string Usage => "perm <path> | perm grant <path> <everyone|account|group:name> <read|write|read,write> | perm revoke <path> <who>";

    public int MinArguments => 1;

    public int MaxArguments => 4;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        IReadOnlyList<string> args = context.Args;

        return Task.FromResult(FileCommandHelpers.Run(context, () => (args[0].ToLowerInvariant(), args.Count) switch
        {
            ("grant", 4) => Change(context, args[1], args[2], args[3]),
            ("revoke", 3) => Change(context, args[1], args[2], rights: null),
            (_, 1) => Show(context, args[0]),
            _ => context.UsageError(),
        }));
    }

    private static int Show(CommandContext context, string path)
    {
        string canonical = Canonical(context, path);
        Caller caller = context.Caller;

        context.Console.WriteLine($"{canonical}: {caller.Account.Name} may {Describe(Permissions.GetAccess(caller, canonical))}" +
            $" (built-in rules alone: {Describe(Permissions.GetBuiltInAccess(caller, canonical))}).");

        if (PermissionRules.GetDeciding(canonical) is { } deciding)
        {
            context.Console.WriteLine($"Rules from {deciding}:");
            foreach (PermissionRule rule in PermissionRules.All.Where(r => string.Equals(r.Path, deciding, StringComparison.OrdinalIgnoreCase)))
            {
                context.Console.WriteLine($"  {rule.Subject.Describe(),-20} {PermissionRules.FormatRights(rule.Rights)}");
            }
        }
        else
        {
            context.Console.WriteLine("No rules from the rules file apply; only the built-in ones.");
        }

        return CommandResult.Success;
    }

    private static int Change(CommandContext context, string path, string who, string? rights)
    {
        string canonical = Canonical(context, path);
        if (!File.Exists(canonical) && !Directory.Exists(canonical))
        {
            return context.Fail("No such file or directory: " + path);
        }

        // Administrators anywhere, owners inside their own profile.
        Caller caller = context.Caller;
        bool ownsIt = caller.Account.ProfileFolder is { } profile && Permissions.IsUnder(canonical, profile);
        if (!ownsIt && caller.DenyUnlessAdmin("change permissions outside your own profile") is { } denied)
        {
            return context.Fail(denied);
        }

        if (ParseSubject(who) is not { } subject)
        {
            return context.Fail($"No account or group called {who} (use everyone, an account's name, or group:<name>).");
        }

        if (rights is null)
        {
            if (PermissionRules.Revoke(canonical, subject) is { } revokeError)
            {
                return context.Fail(revokeError);
            }

            context.Console.WriteLine($"Removed what the rules gave {subject.Describe()} on {canonical}.");
            return CommandResult.Success;
        }

        if (PermissionRules.ParseRights(rights) is not { } access)
        {
            return context.UsageError("Rights are read, write or read,write.");
        }

        if (PermissionRules.Grant(canonical, subject, access) is { } grantError)
        {
            return context.Fail(grantError);
        }

        context.Console.WriteLine($"{subject.Describe()} may now {Describe(access)} {canonical} and everything in it.");
        return CommandResult.Success;
    }

    private static string Canonical(CommandContext context, string path)
    {
        return Permissions.Canonicalize(context.ResolvePath(path), "/") ?? throw new FileNotFoundException("No such file or directory: " + path);
    }

    private static RuleSubject? ParseSubject(string who)
    {
        if (who.Equals("everyone", StringComparison.OrdinalIgnoreCase))
        {
            return RuleSubject.Everyone;
        }

        if (who.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
        {
            return GroupStore.Find(who["group:".Length..]) is { } group ? new RuleSubject(RuleSubjectKind.Group, group.Id) : null;
        }

        return AccountStore.Find(who) is { } account ? new RuleSubject(RuleSubjectKind.Account, account.Id) : null;
    }

    private static string Describe(Access access)
    {
        return access switch
        {
            Access.ReadWrite => "read and write",
            Access.Read => "read",
            Access.Write => "write",
            _ => "do nothing with",
        };
    }
}
