using Cosmos.Kernel.System.Input;
using Cosmos.Kernel.System.Input.Layouts;

using RemSox.Kernel.Accounts;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;
using RemSox.Kernel.Programs;

namespace RemSox.Kernel.Configuration;

/// <summary>Where a setting may be set.</summary>
public enum SettingScope
{
    /// <summary>Only for the machine: it applies before anyone logs in, or to everyone at once.</summary>
    Machine,

    /// <summary>For the machine, and by each account for itself, over the machine's value.</summary>
    Account,
}

/// <summary>Where the value of a setting comes from.</summary>
public enum SettingSource
{
    /// <summary>The account's own settings file.</summary>
    Account,

    /// <summary>The machine's settings file.</summary>
    Machine,

    /// <summary>The built-in default: no file sets it.</summary>
    Default,
}

/// <summary>
/// The settings that survive a reboot. The machine's are kept in
/// <see cref="SystemPaths.SettingsFile"/>: one <c>key = value</c> per line, <c>#</c> starting a
/// comment. Settings of <see cref="SettingScope.Account"/> can also be set by each account for
/// itself (<see cref="AccountSettings"/>), and are read for an account with
/// <see cref="Get(string, Account)"/>: its own value, else the machine's, else the default. Servers to start at boot go in the
/// service list instead (<see cref="SystemPaths.ServicesFile"/>), and commands to run at login
/// in a user's startup script.
/// </summary>
/// <remarks>
/// Changes rewrite only the lines they touch, so comments and the order of the file are kept.
/// Without a mounted disk the defaults apply and nothing can be saved. The settings are the whole
/// system's, so anyone may read them and only a caller with admin rights may change them: the
/// check is here, not in each place that changes one.
/// </remarks>
public static class SystemSettings
{
    public const string KeyboardLayoutKey = "keyboard.layout";
    public const string LogFileKey = "log.file";
    public const string PathKey = "path";
    public const string PathExtensionsKey = "pathext";
    public const string LoginScreenKey = "login.screen";

    /// <summary>The <c>login.screen</c> values: the console prompt, or the login window.</summary>
    public const string TextLogin = "text";

    /// <inheritdoc cref="TextLogin"/>
    public const string GraphicalLogin = "graphical";

    /// <summary>Separates the folders of the <c>path</c> setting, as in Windows' PATH.</summary>
    public const char PathSeparator = ';';

    // Declared before Known, whose initializer lists these names.
    private static readonly Dictionary<string, Func<KeyboardLayout>> Layouts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["us"] = () => new USStandardLayout(),
        ["us-dvorak"] = () => new USDvorakLayout(),
        ["de"] = () => new GermanKeyboardLayout(), // Cosmos's DEStandardLayout maps # and < wrong
        ["fr"] = () => new FRStandardLayout(),
        ["gb"] = () => new GBStandardLayout(),
        ["es"] = () => new ESStandardLayout(),
        ["tr"] = () => new TRStandardLayout(),
    };

    /// <summary>
    /// The settings <c>config</c> knows: name, default, description and where they may be set.
    /// The <c>open.&lt;extension&gt;</c> file associations are settings too, of
    /// <see cref="SettingScope.Account"/>, owned by <see cref="FileTypes"/>.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Default, string Description, SettingScope Scope)> Known =
    [
        (KeyboardLayoutKey, "us", "layout of the keyboard: " + string.Join(", ", Layouts.Keys), SettingScope.Account),
        (LogFileKey, SystemPaths.DefaultLogFile, "where the system log is kept across reboots; empty for none", SettingScope.Machine),
        (PathKey, SystemPaths.ProgramsFolder, "folders searched after the current one, separated by ; (an account's own come after these; by default its programs folder)", SettingScope.Account),
        (PathExtensionsKey, ".rsh;.ynt;.rsl", "extensions tried in order for a name without one, separated by ;", SettingScope.Machine),
        (LoginScreenKey, TextLogin, $"how the machine greets you: {TextLogin} (the console prompt) or {GraphicalLogin} (a login window that starts the desktop)", SettingScope.Machine),
    ];

    private static KeyValueFile file = new([]);

    /// <summary>Gets whether settings can be saved (a disk is mounted).</summary>
    public static bool CanSave => FileSystemManager.IsMounted;

    /// <summary>
    /// Reads the settings file (creating it when it is missing), writes every setting the file
    /// does not have yet into it with its built-in default, and applies the settings. So the
    /// file always shows every setting in effect; lines already in it are never changed, even
    /// invalid ones. Runs at boot and again on <c>config reload</c>, so a file edited by hand
    /// takes effect without a reboot.
    /// </summary>
    /// <returns>
    /// The problems found (also logged): a file that could not be read keeps the settings
    /// already in effect, an invalid value its default, and an unknown setting is ignored.
    /// </returns>
    public static IReadOnlyList<string> Load()
    {
        List<string> problems = [];

        if (!CanSave)
        {
            file = new([]);
            return problems;
        }

        try
        {
            bool exists = File.Exists(SystemPaths.SettingsFile);
            // Replaced only once read: a reload that fails keeps the settings in effect.
            file = new(exists ? File.ReadAllLines(SystemPaths.SettingsFile) : FileHeader);

            if (AddMissingLocked() || !exists)
            {
                SaveLocked();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add("could not read or update the file: " + ex.Message);
        }

        foreach ((string key, string value) in file.Entries)
        {
            if (!IsKnown(key))
            {
                problems.Add($"unknown setting '{key}' ignored");
            }
            else if (!Known.Any(k => k.Key == key) && Validate(key, value) is { } error)
            {
                problems.Add($"{key}: {error}; ignored");
            }
        }

        foreach ((string key, string _, string _, SettingScope _) in Known)
        {
            if (Validate(key, Get(key)) is { } error)
            {
                problems.Add($"{key}: {error}; using the default");
            }

            Apply(key);
        }

        foreach (string problem in problems)
        {
            SystemLog.Log($"{SystemPaths.SettingsFile}: {problem}", LogSeverity.Warning);
        }

        return problems;
    }

    /// <summary>Returns a setting's value for the machine (what everyone gets): the machine's file, else its default.</summary>
    public static string Get(string key)
    {
        return file.Get(key) ?? GetDefault(key);
    }

    /// <summary>
    /// Returns a setting's value for <paramref name="account"/>: for a setting of
    /// <see cref="SettingScope.Account"/> its own valid value, else the machine's.
    /// </summary>
    public static string Get(string key, Account account)
    {
        return GetAccountValue(key, account) ?? Get(key);
    }

    /// <summary>Returns where <paramref name="account"/>'s value of a setting comes from.</summary>
    public static SettingSource GetSource(string key, Account account)
    {
        return GetAccountValue(key, account) is not null ? SettingSource.Account
            : file.Contains(key) ? SettingSource.Machine
            : SettingSource.Default;
    }

    /// <summary>Returns where a setting may be set: unknown settings count as the machine's.</summary>
    public static SettingScope GetScope(string key)
    {
        return key.StartsWith(FileTypes.AssociationKeyPrefix)
            ? SettingScope.Account
            : Known.FirstOrDefault(k => k.Key == key).Scope;
    }

    /// <summary>Returns the account's own value of a setting it may set, when valid; else null.</summary>
    private static string? GetAccountValue(string key, Account account)
    {
        if (GetScope(key) != SettingScope.Account || AccountSettings.GetPath(account) is null)
        {
            return null;
        }

        return AccountSettings.Get(account).Get(key) is { } value && Validate(key, value) is null ? value : null;
    }

    /// <summary>
    /// Returns whether <paramref name="key"/> names a setting: a known one, or one of the
    /// <c>open.&lt;extension&gt;</c> file associations, which <see cref="FileTypes"/> owns.
    /// </summary>
    public static bool IsKnown(string key)
    {
        return Known.Any(k => k.Key == key) || key.StartsWith(FileTypes.AssociationKeyPrefix);
    }

    /// <summary>Returns the keys the machine's file, or the account's, sets that start with <paramref name="prefix"/>.</summary>
    public static IEnumerable<string> GetKeys(string prefix, Account account)
    {
        IEnumerable<string> accountKeys = AccountSettings.GetPath(account) is null ? [] : AccountSettings.Get(account).Entries.Select(e => e.Key);
        return file.Entries.Select(e => e.Key).Concat(accountKeys).Where(key => key.StartsWith(prefix)).Distinct();
    }

    /// <summary>
    /// Returns the extensions of the <c>pathext</c> setting, in order: the ones a name typed
    /// without an extension is tried with.
    /// </summary>
    public static IReadOnlyList<string> GetPathExtensions()
    {
        string value = Get(PathExtensionsKey);
        if (Validate(PathExtensionsKey, value) is not null)
        {
            value = GetDefault(PathExtensionsKey);
        }

        return value.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string GetDefault(string key)
    {
        if (key.StartsWith(FileTypes.AssociationKeyPrefix))
        {
            return FileTypes.GetDefaultOpener(key[FileTypes.AssociationKeyPrefix.Length..]) ?? string.Empty;
        }

        return Known.FirstOrDefault(k => k.Key == key).Default ?? string.Empty;
    }

    /// <summary>
    /// Returns the folders the shell searches for <paramref name="account"/> after the current
    /// one, in order: the machine's <c>path</c>, then the account's own, which adds to the
    /// machine's instead of replacing it (by default the account's programs folder).
    /// </summary>
    public static IReadOnlyList<string> GetPathFolders(Account account)
    {
        string machine = Get(PathKey);
        if (Validate(PathKey, machine) is not null)
        {
            machine = SystemPaths.ProgramsFolder;
        }

        string own = GetAccountValue(PathKey, account) ?? account.ProgramsFolder ?? string.Empty;
        return [.. SplitList(machine), .. SplitList(own)];
    }

    private static string[] SplitList(string value)
    {
        return value.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Returns whether the file sets <paramref name="key"/>. After <see cref="Load"/> it sets
    /// every setting that has a default, unless there is no disk to keep the file on.
    /// </summary>
    public static bool IsSet(string key)
    {
        return file.Contains(key);
    }

    /// <summary>Returns why <paramref name="value"/> is not valid for <paramref name="key"/>, or null when it is.</summary>
    public static string? Validate(string key, string value)
    {
        return key switch
        {
            KeyboardLayoutKey when !Layouts.ContainsKey(value) => $"unknown layout '{value}' (use {string.Join(", ", Layouts.Keys)})",
            LogFileKey when value.Length > 0 && !value.StartsWith('/') => "an absolute path, or empty for none",
            PathKey when value.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(folder => !folder.StartsWith('/'))
                => "absolute folders separated by " + PathSeparator,
            PathExtensionsKey when value.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is var extensions
                && extensions.Any(extension => extension.Length < 2 || extension[0] != '.' || extension.IndexOf('.', 1) >= 0)
                => "extensions such as .rsh, separated by " + PathSeparator,
            LoginScreenKey when value is not (TextLogin or GraphicalLogin) => $"{TextLogin} or {GraphicalLogin}",
            KeyboardLayoutKey or LogFileKey or PathKey or PathExtensionsKey or LoginScreenKey => null,
            _ when key.StartsWith(FileTypes.AssociationKeyPrefix) => FileTypes.ValidateAssociation(key[FileTypes.AssociationKeyPrefix.Length..], value),
            _ => $"unknown setting '{key}' (known: {string.Join(", ", Known.Select(k => k.Key))}, {FileTypes.AssociationKeyPrefix}<extension>)",
        };
    }

    /// <summary>
    /// Validates, saves and applies a setting. An empty value turns a setting off (no log file,
    /// no association, no extra folders or extensions) where that makes sense.
    /// </summary>
    /// <returns>Null on success, else why it failed (also when <paramref name="caller"/> may not change settings).</returns>
    public static string? Set(Caller caller, string key, string value)
    {
        if (caller.DenyUnlessAdmin("change the settings") is { } denied)
        {
            return denied;
        }

        if (Validate(key, value) is { } error)
        {
            return error;
        }

        if (WithFile(() => file.Set(key, value)) is { } saveError)
        {
            return saveError;
        }

        Apply(key);
        return null;
    }

    /// <summary>
    /// Sets a setting back to its built-in default, written into the file like any value. A
    /// setting without a default (an association the user added) is removed from the file.
    /// </summary>
    /// <returns>Null on success, else why it failed (also when <paramref name="caller"/> may not change settings).</returns>
    public static string? Reset(Caller caller, string key)
    {
        if (caller.DenyUnlessAdmin("change the settings") is { } denied)
        {
            return denied;
        }

        if (!IsKnown(key))
        {
            return Validate(key, string.Empty);
        }

        if (GetDefault(key) is { Length: > 0 } defaultValue)
        {
            return Set(caller, key, defaultValue);
        }

        if (WithFile(() => file.Remove(key)) is { } error)
        {
            return error;
        }

        Apply(key);
        return null;
    }

    /// <summary>Applies a setting's current value to the running system.</summary>
    private static void Apply(string key)
    {
        string value = Get(key);
        if (Validate(key, value) is not null)
        {
            value = GetDefault(key);
        }

        switch (key)
        {
            case KeyboardLayoutKey:
                if (KeyboardManager.IsEnabled)
                {
                    KeyboardManager.SetLayout(Layouts[value]());
                }
                break;

            case LogFileKey:
                SystemLog.SetFile(value.Length == 0 || !CanSave ? null : value);
                break;
        }
    }

    /// <summary>Changes the lines and saves them, or reports why it could not.</summary>
    private static string? WithFile(Action change)
    {
        if (!CanSave)
        {
            return FileSystemManager.NoDiskMessage;
        }

        KeyValueFile before = file.Copy();
        change();

        try
        {
            SaveLocked();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            file = before;
            return $"Could not save {SystemPaths.SettingsFile}: {ex.Message}";
        }
    }

    private static void SaveLocked()
    {
        SafeFile.WriteAllLines(SystemPaths.SettingsFile, file.Lines);
    }

    /// <summary>
    /// Appends each setting the file lacks (a new file, or one from before the setting existed)
    /// with its default and a comment saying what it does.
    /// </summary>
    /// <returns>Whether anything was added, so the file needs saving.</returns>
    private static bool AddMissingLocked()
    {
        HashSet<string> present = [.. file.Entries.Select(e => e.Key)];
        int count = file.Lines.Count;
        List<string> added = [];

        foreach ((string key, string defaultValue, string description, SettingScope _) in Known.Where(k => !present.Contains(k.Key)))
        {
            file.Append(["#", $"# {key}: {description}", $"{key} = {defaultValue}"]);
            added.Add(key);
        }

        List<(string Name, string Opener)> associations = [.. FileTypes.GetDefaultAssociations()
            .Where(association => !present.Contains(FileTypes.AssociationKeyPrefix + association.Name))];

        if (associations.Count > 0 && !present.Any(key => key.StartsWith(FileTypes.AssociationKeyPrefix)))
        {
            file.Append([
                "#",
                $"# {FileTypes.AssociationKeyPrefix}<extension>: the command or program that opens such files when",
                "# their name is typed or they are started from the start menu; empty for none.",
            ]);
        }

        foreach ((string name, string opener) in associations)
        {
            file.Append([$"{FileTypes.AssociationKeyPrefix}{name} = {opener}"]);
            added.Add(FileTypes.AssociationKeyPrefix + name);
        }

        if (added.Count > 0 && count > FileHeader.Length)
        {
            SystemLog.Log($"{SystemPaths.SettingsFile}: added {string.Join(", ", added)} with their defaults", LogSeverity.Info);
        }

        return added.Count > 0;
    }

    private static readonly string[] FileHeader =
    [
        "# RemSox settings, read at boot and by 'config reload'. Change them with",
        "# 'config set' or by editing this file; '#' starts a comment. Settings",
        "# missing here are added with their defaults; 'config reset <key>' sets one",
        "# back to its default. Services to start at boot are in " + SystemPaths.ServicesFile + ".",
    ];
}
