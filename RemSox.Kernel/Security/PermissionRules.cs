using System.Globalization;

using RemSox.Kernel.Accounts;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;
using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Security;

/// <summary>Whom a rule grants rights to.</summary>
public enum RuleSubjectKind
{
    /// <summary>Every logged-in account.</summary>
    Everyone,

    Account,

    Group,
}

/// <summary>Whom a rule grants rights to: everyone, or one account or group by its id.</summary>
public readonly record struct RuleSubject(RuleSubjectKind Kind, int Id)
{
    public static RuleSubject Everyone => new(RuleSubjectKind.Everyone, 0);

    /// <summary>Gets whether the rule applies to <paramref name="account"/>.</summary>
    public bool Includes(Account account)
    {
        return Kind switch
        {
            RuleSubjectKind.Everyone => true,
            RuleSubjectKind.Account => account.Id == Id,
            _ => account.IsInGroup(Id),
        };
    }

    /// <summary>How the rules file writes it: <c>everyone</c>, <c>account:3</c>, <c>group:0</c>.</summary>
    public override string ToString()
    {
        return Kind switch
        {
            RuleSubjectKind.Everyone => "everyone",
            RuleSubjectKind.Account => "account:" + Id.ToString(CultureInfo.InvariantCulture),
            _ => "group:" + Id.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>How people read it: <c>everyone</c>, an account's name, <c>group:dev</c>.</summary>
    public string Describe()
    {
        return Kind switch
        {
            RuleSubjectKind.Everyone => "everyone",
            RuleSubjectKind.Account => AccountStore.Find(Id)?.Name ?? ToString(),
            _ => "group:" + (GroupStore.Find(Id)?.Name ?? Id.ToString(CultureInfo.InvariantCulture)),
        };
    }

    /// <summary>Reads what the rules file writes; null when it is not that.</summary>
    public static RuleSubject? Parse(string text)
    {
        if (text == "everyone")
        {
            return Everyone;
        }

        string[] parts = text.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int id))
        {
            return null;
        }

        return parts[0] switch
        {
            "account" => new RuleSubject(RuleSubjectKind.Account, id),
            "group" => new RuleSubject(RuleSubjectKind.Group, id),
            _ => null,
        };
    }
}

/// <summary>One line of the rules file: rights on a folder (and everything in it) for someone.</summary>
public sealed record PermissionRule(string Path, RuleSubject Subject, Access Rights);

/// <summary>
/// The rules administrators (and owners, inside their profile) add to the built-in ones, kept in
/// <see cref="SystemPaths.PermissionsFile"/>: <c>path who rights</c> per line. The most specific
/// path with rules decides; the rules there add to the built-in rights, never take any away, and
/// nothing can be granted on <see cref="SystemPaths.SecretsFolder"/>.
/// </summary>
public static class PermissionRules
{
    private static readonly List<PermissionRule> rules = [];

    private static readonly string[] FileHeader =
    [
        "# RemSox permission rules, adding to the built-in ones: path (quoted if it has spaces), who",
        "# (everyone, account:<id> or group:<id>) and rights (read, write or read,write). The most",
        "# specific path with rules decides. Change them with 'perm grant' and 'perm revoke'.",
    ];

    /// <summary>Gets every rule, in the order of the file.</summary>
    public static IReadOnlyList<PermissionRule> All => rules;

    /// <summary>Reads the rules file; a line that cannot be read is logged and skipped.</summary>
    public static void Load()
    {
        rules.Clear();

        if (!FileSystemManager.IsMounted || !File.Exists(SystemPaths.PermissionsFile))
        {
            return;
        }

        try
        {
            foreach (string line in File.ReadAllLines(SystemPaths.PermissionsFile))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                {
                    continue;
                }

                // Quoted like a shell line, so a path may contain spaces.
                List<string> parts = CommandLine.SplitWords(trimmed);
                if (parts.Count != 3 || !parts[0].StartsWith('/')
                    || RuleSubject.Parse(parts[1]) is not { } subject
                    || ParseRights(parts[2]) is not { } rights)
                {
                    SystemLog.Log($"{SystemPaths.PermissionsFile}: ignored the line \"{line}\"", LogSeverity.Warning);
                    continue;
                }

                rules.Add(new PermissionRule(parts[0], subject, rights));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SystemLog.Log($"Could not read {SystemPaths.PermissionsFile}: {ex.Message}", LogSeverity.Error);
        }
    }

    /// <summary>Returns the rights the rules give <paramref name="account"/> on <paramref name="canonicalPath"/>.</summary>
    public static Access GetAccess(Account account, string canonicalPath)
    {
        if (Permissions.IsUnder(canonicalPath, SystemPaths.SecretsFolder) || GetDeciding(canonicalPath) is not { } path)
        {
            return Access.None;
        }

        Access rights = Access.None;
        foreach (PermissionRule rule in rules)
        {
            if (string.Equals(rule.Path, path, StringComparison.OrdinalIgnoreCase) && rule.Subject.Includes(account))
            {
                rights |= rule.Rights;
            }
        }

        return rights;
    }

    /// <summary>Returns the most specific path with rules that <paramref name="canonicalPath"/> is in, or null.</summary>
    public static string? GetDeciding(string canonicalPath)
    {
        return rules
            .Where(rule => Permissions.IsUnder(canonicalPath, rule.Path))
            .Select(rule => rule.Path)
            .OrderByDescending(path => path.Length)
            .FirstOrDefault();
    }

    /// <summary>Gives <paramref name="subject"/> <paramref name="rights"/> on <paramref name="canonicalPath"/>, replacing what a rule there gave them.</summary>
    /// <returns>Null, or why not.</returns>
    public static string? Grant(string canonicalPath, RuleSubject subject, Access rights)
    {
        MainLoop.AssertMainThread(nameof(Grant));

        if (Permissions.IsUnder(canonicalPath, SystemPaths.SecretsFolder))
        {
            return $"Nothing can be granted on {SystemPaths.SecretsFolder}.";
        }

        List<PermissionRule> before = [.. rules];
        _ = rules.RemoveAll(rule => string.Equals(rule.Path, canonicalPath, StringComparison.OrdinalIgnoreCase) && rule.Subject == subject);
        rules.Add(new PermissionRule(canonicalPath, subject, rights));
        return SaveOrRestore(before);
    }

    /// <summary>Removes what the rules give <paramref name="subject"/> on <paramref name="canonicalPath"/>.</summary>
    /// <returns>Null, or why not.</returns>
    public static string? Revoke(string canonicalPath, RuleSubject subject)
    {
        MainLoop.AssertMainThread(nameof(Revoke));

        List<PermissionRule> before = [.. rules];
        if (rules.RemoveAll(rule => string.Equals(rule.Path, canonicalPath, StringComparison.OrdinalIgnoreCase) && rule.Subject == subject) == 0)
        {
            return $"No rule on {canonicalPath} names {subject.Describe()}.";
        }

        return SaveOrRestore(before);
    }

    /// <summary>Reads <c>read</c>, <c>write</c> or <c>read,write</c>; null for anything else.</summary>
    public static Access? ParseRights(string text)
    {
        Access rights = Access.None;
        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            rights |= part.ToLowerInvariant() switch
            {
                "read" => Access.Read,
                "write" => Access.Write,
                _ => (Access)(-1),
            };
        }

        return rights is Access.None or < 0 or > Access.ReadWrite ? null : rights;
    }

    /// <summary>Writes rights the way the file and <c>perm</c> do: <c>read</c>, <c>write</c>, <c>read,write</c> or <c>none</c>.</summary>
    public static string FormatRights(Access rights)
    {
        return rights switch
        {
            Access.ReadWrite => "read,write",
            Access.Read => "read",
            Access.Write => "write",
            _ => "none",
        };
    }

    private static string? SaveOrRestore(List<PermissionRule> before)
    {
        try
        {
            SafeFile.WriteAllLines(SystemPaths.PermissionsFile, [
                .. FileHeader,
                .. rules.Select(rule => $"{CommandLine.Quote(rule.Path)} {rule.Subject} {FormatRights(rule.Rights)}"),
            ]);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            rules.Clear();
            rules.AddRange(before);
            return $"Could not save {SystemPaths.PermissionsFile}: {ex.Message}";
        }
    }
}
