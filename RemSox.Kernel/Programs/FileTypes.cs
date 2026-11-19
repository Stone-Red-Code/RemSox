using RemSox.Kernel.Configuration;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Programs;

/// <summary>
/// What opens a file of each type. Scripts (<c>.rsh</c>) and shortcuts (<c>.rsl</c>) are built
/// in, as <c>.bat</c> and <c>.lnk</c> are on Windows; every other type opens with the command or
/// program its file association names, the <c>open.&lt;extension&gt;</c> setting (empty for
/// none, which also turns off a default association). How a file
/// is then started is <see cref="Launcher"/>'s decision.
/// </summary>
/// <remarks>
/// The opener gets the file's full path as its first argument, then any arguments typed after it.
/// </remarks>
public static class FileTypes
{
    /// <summary>Starts the settings that associate an extension with its opener (<c>open.txt = edit</c>).</summary>
    public const string AssociationKeyPrefix = "open.";

    /// <summary>The associations that apply while the settings do not set <c>open.&lt;extension&gt;</c>.</summary>
    private static readonly Dictionary<string, string> DefaultAssociations = new(StringComparer.OrdinalIgnoreCase)
    {
        [".ynt"] = "yesnt",
        [".txt"] = "edit",
        [".conf"] = "edit",
        [".log"] = "edit",
    };

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

    /// <summary>Returns whether <paramref name="path"/> can be opened: a script, a shortcut or a file with an association.</summary>
    public static bool CanOpen(string path)
    {
        return IsScript(path) || IsShortcut(path) || GetOpener(path) is not null;
    }

    /// <summary>
    /// Returns the command or program associated with <paramref name="path"/>'s extension, or null
    /// when it has none (always for scripts and shortcuts). An invalid setting counts as none.
    /// </summary>
    public static string? GetOpener(string path)
    {
        string extension = Path.GetExtension(path);
        if (extension.Length < 2)
        {
            return null;
        }

        string name = extension[1..].ToLowerInvariant();
        string value = SystemSettings.Get(AssociationKeyPrefix + name);
        return value.Length == 0 || ValidateAssociation(name, value) is not null ? null : value;
    }

    /// <summary>
    /// Returns every association by extension: the defaults, changed by the settings. An empty
    /// opener means the extension has none; invalid settings are left out.
    /// </summary>
    public static IReadOnlyList<(string Extension, string Opener, bool IsDefault)> GetAssociations()
    {
        IEnumerable<string> extensions = DefaultAssociations.Keys
            .Concat(SystemSettings.GetKeys(AssociationKeyPrefix).Select(key => "." + key[AssociationKeyPrefix.Length..]))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        List<(string Extension, string Opener, bool IsDefault)> associations = [];
        foreach (string extension in extensions)
        {
            string name = extension[1..].ToLowerInvariant();
            string opener = SystemSettings.Get(AssociationKeyPrefix + name);
            if (ValidateAssociation(name, opener) is null)
            {
                associations.Add((extension, opener, !SystemSettings.IsSet(AssociationKeyPrefix + name)));
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

        // The Terminal runs its arguments as a typed line, which would open the file in another
        // Terminal, and so on without end.
        if (value.Equals(BuiltInPrograms.TerminalName, StringComparison.OrdinalIgnoreCase))
        {
            return $"{value} runs a command line; it cannot open files";
        }

        return value.Length == 0 || CommandManager.Find(value) is not null || ProgramRegistry.Find(value) is not null
            ? null
            : $"'{value}' is not a command or program (leave it empty for no association)";
    }
}
