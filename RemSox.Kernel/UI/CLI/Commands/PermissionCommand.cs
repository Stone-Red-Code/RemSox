using RemSox.Kernel.Accounts;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Security;

namespace RemSox.Kernel.UI.CLI.Commands;

/// <summary>
/// Shows who may do what with a path and why, and adds or removes rules (see
/// <see cref="PermissionRules"/>): anywhere for an administrator with <c>admin</c>, inside their
/// own profile for its owner, who can share a folder of theirs this way.
/// </summary>
public sealed class PermissionCommand : ICommand
{
    public string Name => "perm";

    public string Description => "Show who may read or write a path, or grant and revoke rights on it";

    public string Usage => "perm show <path> | perm grant <path> <everyone|user:name|group:name> <read|write|read,write> | perm revoke <path> <who>";

    public int MinArguments => 2;

    public int MaxArguments => 4;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        IReadOnlyList<string> args = context.Args;

        return Task.FromResult(FileCommandHelpers.Run(context, () => (args[0].ToLowerInvariant(), args.Count) switch
        {
            ("grant", 4) => Change(context, args[1], args[2], args[3]),
            ("revoke", 3) => Change(context, args[1], args[2], rights: null),
            ("show", 2) => Show(context, args[1]),
            _ => context.UsageError(),
        }));
    }

    /// <summary>Lists who may do what with the path: one line per account, group or everyone.</summary>
    private static int Show(CommandContext context, string path)
    {
        string canonical = Canonical(context, path);
        IReadOnlyList<(RuleSubject Subject, Access Rights)> overview = Permissions.GetOverview(canonical);

        context.Console.WriteLine(canonical);
        foreach ((RuleSubject subject, Access rights) in overview)
        {
            // Being in admins counts only with admin rights, so say so where it is listed.
            string note = subject == new RuleSubject(RuleSubjectKind.Group, Group.AdminsId) ? "  (with admin)" : string.Empty;
            context.Console.WriteLine($"  {subject.Describe(),-20} {PermissionRules.FormatRights(rights).Replace(",", ", ")}{note}");
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
            return context.Fail($"No such user or group: {who} (use everyone, user:<name> or group:<name>).");
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

    /// <summary>
    /// Reads who a rule is for: <c>everyone</c>, <c>user:name</c> or <c>group:name</c>. Accounts
    /// and groups always carry their kind, so no name can be mistaken for another kind of subject.
    /// </summary>
    private static RuleSubject? ParseSubject(string who)
    {
        if (who.Equals("everyone", StringComparison.OrdinalIgnoreCase))
        {
            return RuleSubject.Everyone;
        }

        int colon = who.IndexOf(':');
        if (colon < 0)
        {
            return null;
        }

        string name = who[(colon + 1)..];
        return who[..colon].ToLowerInvariant() switch
        {
            "user" => AccountStore.Find(name) is { } account ? new RuleSubject(RuleSubjectKind.Account, account.Id) : null,
            "group" => GroupStore.Find(name) is { } group ? new RuleSubject(RuleSubjectKind.Group, group.Id) : null,
            _ => null,
        };
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
