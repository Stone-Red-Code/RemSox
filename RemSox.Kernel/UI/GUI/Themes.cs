using RemSox.Kernel.Accounts;
using RemSox.Kernel.Configuration;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;
using RemSox.Kernel.Security;

namespace RemSox.Kernel.UI.GUI;

/// <summary>
/// Finds the theme an account uses (design: <c>docs/design/theming.md</c>): the one its
/// <c>theme</c> setting names, from its own themes folder (<see cref="Account.ThemesFolder"/>),
/// else <see cref="SystemPaths.ThemesFolder"/>, else the built-in one, with the <c>accent</c>
/// setting over it. A theme file's missing values come from its base.
/// </summary>
public static class Themes
{
    // Where a theme is looked for, in order; a file whose base has its own name continues at the next.
    private enum Layer { Account, System, BuiltIn }

    /// <summary>
    /// Writes each built-in theme to <see cref="SystemPaths.ThemesFolder"/> as a file showing its
    /// values as comments (<see cref="Theme.ToCommentedLines"/>), when it is missing or still sets
    /// nothing, so it shows the values of the RemSox running; a file an administrator changed
    /// stays.
    /// </summary>
    public static void WriteBuiltInFiles()
    {
        foreach (Theme theme in Theme.BuiltIn)
        {
            string path = GetSystemPath(theme.Name);
            string[] lines = [.. theme.ToCommentedLines()];
            if (File.Exists(path))
            {
                string[] current = File.ReadAllLines(path);
                if (new KeyValueFile(current).Entries.Any() || current.SequenceEqual(lines))
                {
                    continue;
                }
            }

            SafeFile.WriteAllLines(path, lines);
        }
    }

    /// <summary>
    /// Returns the theme <paramref name="caller"/>'s account uses: its <c>theme</c> setting's,
    /// with its <c>accent</c> over it. Files are read with the caller's rights (the system's,
    /// for the login window). A theme that cannot be found or read is logged, and the built-in
    /// dark theme used.
    /// </summary>
    public static Theme Load(Caller caller)
    {
        Account account = caller.Account;
        string name = SystemSettings.Get(SystemSettings.ThemeKey, account);
        Theme theme = Find(name, Layer.Account, caller, []) ?? Report(account, $"There is no theme '{name}'; using {Theme.DarkName}.");

        return SystemSettings.GetAccent(account) is { } accent ? theme.WithAccent(accent) : theme;
    }

    /// <summary>Returns the theme called <paramref name="name"/> from <paramref name="layer"/> on, or null when there is none.</summary>
    private static Theme? Find(string name, Layer layer, Caller caller, HashSet<string> resolving)
    {
        for (; layer <= Layer.BuiltIn; layer++)
        {
            if (layer == Layer.BuiltIn)
            {
                return Theme.GetBuiltIn(name);
            }

            if (Read(name, layer, caller) is not { } file)
            {
                continue;
            }

            // The file's values over its base's: the theme it names, else the next layer's of
            // the same name (a changed "dark" over the built-in one), else the dark theme.
            string key = layer + "/" + name;
            if (!resolving.Add(key))
            {
                return Report(caller.Account, $"The theme '{name}' is its own base; using {Theme.DarkName}.");
            }

            string? baseName = Theme.GetBaseName(file.Lines);
            Theme baseTheme = baseName is null || baseName == name
                ? Find(name, layer + 1, caller, resolving) ?? Theme.Dark
                : Find(baseName, Layer.Account, caller, resolving) ?? Report(caller.Account, $"The theme '{name}' is based on '{baseName}', which does not exist; using {Theme.DarkName}.");

            List<string> problems = [];
            Theme theme = Theme.Parse(name, file.Lines, baseTheme, problems);
            foreach (string problem in problems)
            {
                Report(layer == Layer.Account ? caller.Account : null, $"{file.Path}: {problem}");
            }

            _ = resolving.Remove(key);
            return theme;
        }

        return null;
    }

    /// <summary>Reads theme <paramref name="name"/>'s file in <paramref name="layer"/>'s folder, or null when it has none or it cannot be read (logged).</summary>
    private static (string Path, KeyValueFile Lines)? Read(string name, Layer layer, Caller caller)
    {
        if (Theme.ValidateName(name) is not null || !FileSystemManager.IsMounted)
        {
            return null;
        }

        string? path = layer == Layer.System ? GetSystemPath(name)
            : caller.Account.ThemesFolder is { } own ? own + "/" + name + Theme.FileExtension
            : null;
        if (path is null)
        {
            return null;
        }

        try
        {
            if (layer == Layer.System)
            {
                return File.Exists(path) ? (path, new KeyValueFile(File.ReadAllLines(path))) : null;
            }

            return UserFiles.Exists(caller, path) ? (path, new KeyValueFile(UserFiles.ReadAllLines(caller, path))) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(layer == Layer.Account ? caller.Account : null, $"Could not read the theme {path}: {ex.Message}");
            return null;
        }
    }

    private static string GetSystemPath(string name)
    {
        return SystemPaths.ThemesFolder + "/" + name + Theme.FileExtension;
    }

    private static Theme Report(Account? account, string message)
    {
        SystemLog.Log(message, LogSeverity.Warning, accountId: account is { IsSystem: false } ? account.Id : null);
        return Theme.Dark;
    }
}
