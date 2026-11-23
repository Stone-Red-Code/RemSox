using RemSox.Kernel.Accounts;
using RemSox.Kernel.Configuration;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Programs;

/// <summary>
/// What opens a file of each type. Scripts (<c>.rsh</c>) and shortcuts (<c>.rsl</c>) are built
/// in, as <c>.bat</c> and <c>.lnk</c> are on Windows; every other type opens with the command or
/// program its file association names, the <c>open.&lt;extension&gt;</c> setting (empty for
/// none, which also turns off a default association), which each account may set for itself
/// over the machine's. How a file
/// is then started is <see cref="Launcher"/>'s decision.
/// </summary>
/// <remarks>
/// An association is a command line: the opener, then any arguments it always gets, such as
/// <c>open.ynw = noterminal yesnt</c> for scripts that open no Terminal (Windows' <c>.pyw</c>).
/// The opener runs with those arguments, then the file's full path, then any arguments typed
/// after the file.
/// </remarks>
public static class FileTypes
{
    /// <summary>Starts the settings that associate an extension with its opener (<c>open.txt = edit</c>).</summary>
    public const string AssociationKeyPrefix = "open.";

    /// <summary>The associations that apply while the settings do not set <c>open.&lt;extension&gt;</c>.</summary>
    private static readonly Dictionary<string, string> DefaultAssociations = new(StringComparer.OrdinalIgnoreCase)
    {
        [".ynt"] = "yesnt",
        [".txt"] = BuiltInPrograms.TextEditName,
        [".conf"] = BuiltInPrograms.TextEditName,
        [".log"] = BuiltInPrograms.TextEditName,
    };

    /// <summary>
    /// The file types the system opens itself, which no association can change: they are not
    /// "a program started with the file" (a script runs in your shell, a shortcut starts its
    /// target), but they are file types like the others and are listed with them.
    /// </summary>
    public static readonly IReadOnlyList<(string Extension, string Description)> BuiltInTypes =
    [
        (ShellScript.FileExtension, "a shell script: runs in a shell"),
        (Shortcut.FileExtension, "a shortcut: starts its target"),
    ];

    /// <summary>Returns whether <paramref name="path"/> is a shell script, which a shell runs itself.</summary>
    public static bool IsScript(string path)
    {
        return Path.GetExtension(path).Equals(ShellScript.FileExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns whether <paramref name="path"/> is a shortcut, which starts its target when opened.</summary>
    public static bool IsShortcut(string path)
    {
        return Path.GetExtension(path).Equals(Shortcut.FileExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns whether <paramref name="account"/> can open <paramref name="path"/>: a script, a shortcut or a file with an association.</summary>
    public static bool CanOpen(string path, Account account)
    {
        return IsScript(path) || IsShortcut(path) || GetOpener(path, account) is not null;
    }

    /// <summary>
    /// Returns the association of <paramref name="path"/>'s extension for <paramref name="account"/>
    /// as set (the opener and its arguments; <see cref="SplitAssociation"/> parts them), or null
    /// when it has none (always for scripts and shortcuts). An invalid setting counts as none.
    /// </summary>
    public static string? GetOpener(string path, Account account)
    {
        string extension = Path.GetExtension(path);
        if (extension.Length < 2)
        {
            return null;
        }

        string name = extension[1..].ToLowerInvariant();
        string value = SystemSettings.Get(AssociationKeyPrefix + name, account);
        return value.Length == 0 || ValidateAssociation(name, value) is not null ? null : value;
    }

    /// <summary>
    /// Returns every association by extension for <paramref name="account"/>: the defaults,
    /// changed by the machine's settings and the account's own, with where each comes from. An
    /// empty opener means the extension has none; invalid settings are left out.
    /// </summary>
    public static IReadOnlyList<(string Extension, string Opener, SettingSource Source)> GetAssociations(Account account)
    {
        IEnumerable<string> extensions = DefaultAssociations.Keys
            .Concat(SystemSettings.GetKeys(AssociationKeyPrefix, account).Select(key => "." + key[AssociationKeyPrefix.Length..]))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        List<(string Extension, string Opener, SettingSource Source)> associations = [];
        foreach (string extension in extensions)
        {
            string name = extension[1..].ToLowerInvariant();
            string opener = SystemSettings.Get(AssociationKeyPrefix + name, account);
            if (ValidateAssociation(name, opener) is null)
            {
                associations.Add((extension, opener, SystemSettings.GetSource(AssociationKeyPrefix + name, account)));
            }
        }

        return [.. associations.OrderBy(association => association.Extension, StringComparer.Ordinal)];
    }

    /// <summary>Returns the default associations, as the <c>open.&lt;name&gt;</c> setting's name and value.</summary>
    public static IEnumerable<(string Name, string Opener)> GetDefaultAssociations()
    {
        return DefaultAssociations.Select(association => (association.Key[1..], association.Value));
    }

    /// <summary>Returns the default opener for the <c>open.&lt;name&gt;</c> setting, or null when it has none.</summary>
    public static string? GetDefaultOpener(string name)
    {
        return DefaultAssociations.GetValueOrDefault("." + name);
    }

    /// <summary>
    /// Splits an association (<c>noterminal yesnt</c>) into the opener, a command or program,
    /// and the arguments it gets before the file. Words are split as a command line's.
    /// </summary>
    public static (string Opener, IReadOnlyList<string> Arguments) SplitAssociation(string association)
    {
        List<string> words = CommandLine.SplitWords(association);
        return words.Count == 0 ? (string.Empty, []) : (words[0], words[1..]);
    }

    /// <summary>Returns why <paramref name="value"/> cannot be the <c>open.&lt;name&gt;</c> setting, or null when it can.</summary>
    public static string? ValidateAssociation(string name, string value)
    {
        if (name.Length == 0 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return $"'{AssociationKeyPrefix}{name}' is no file association: use {AssociationKeyPrefix}<extension>, such as {AssociationKeyPrefix}txt";
        }

        if (IsScript("x." + name) || IsShortcut("x." + name))
        {
            return $".{name} files are opened by the system itself; this cannot be changed";
        }

        (string opener, _) = SplitAssociation(value);

        // The Terminal runs its arguments as a typed line, which would open the file in another
        // Terminal, and so on without end.
        if (opener.Equals(BuiltInPrograms.TerminalName, StringComparison.OrdinalIgnoreCase))
        {
            return $"{opener} runs a command line; it cannot open files";
        }

        return opener.Length == 0 || CommandManager.Find(opener) is not null || ProgramRegistry.Find(opener) is not null
            ? null
            : $"'{opener}' is not a command or program (leave it empty for no association)";
    }
}
