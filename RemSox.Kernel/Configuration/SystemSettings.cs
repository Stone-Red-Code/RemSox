using System.Globalization;

using Cosmos.Kernel.System.Input;
using Cosmos.Kernel.System.Input.Layouts;

using RemSox.Kernel.Accounts;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;
using RemSox.Kernel.Programs;
using RemSox.Kernel.Security;

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
/// <see cref="Get(string, Account)"/>: its own value, else the machine's, else the default.
/// Servers to start at boot go in the service list instead (<see cref="SystemPaths.ServicesFile"/>), and commands to run at login
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
    public const string LockIdleKey = "lock.idle";

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
        ["fr"] = () => new FrenchKeyboardLayout(), // Cosmos's FRStandardLayout lacks < > and AltGr
        ["gb"] = () => new GBStandardLayout(),
        ["es"] = () => new SpanishKeyboardLayout(), // Cosmos's ESStandardLayout lacks < >
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
        (LockIdleKey, "0", "minutes without input after which a desktop session locks itself; 0 for never", SettingScope.Account),
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
        if (key.StartsWith(FileTypes.AssociationKeyPrefix))
        {
            return SettingScope.Account;
        }

        foreach ((string known, string _, string _, SettingScope scope) in Known)
        {
            if (known == key)
            {
                return scope;
            }
        }

        return SettingScope.Machine;
    }

    /// <summary>Returns the account's own value of a setting it may set, when valid; else null.</summary>
    private static string? GetAccountValue(string key, Account account)
    {
        return GetScope(key) == SettingScope.Account
            && AccountSettings.Get(account).Get(key) is { } value
            && Validate(key, value) is null ? value : null;
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
        return file.Entries.Concat(AccountSettings.Get(account).Entries)
            .Select(e => e.Key).Where(key => key.StartsWith(prefix)).Distinct();
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

    /// <summary>The longest <c>lock.idle</c>, a week: beyond it a typo is likelier than a wish.</summary>
    private const int MaxLockIdleMinutes = 7 * 24 * 60;

    /// <summary>
    /// Returns how long a desktop session of <paramref name="account"/> may go without input
    /// before it locks itself (<c>lock.idle</c>), or null when it never does.
    /// </summary>
    public static TimeSpan? GetLockIdleTime(Account account)
    {
        string value = Get(LockIdleKey, account);
        int minutes = Validate(LockIdleKey, value) is null ? int.Parse(value, CultureInfo.InvariantCulture) : 0;
        return minutes > 0 ? TimeSpan.FromMinutes(minutes) : null;
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
            LockIdleKey when !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) || minutes > MaxLockIdleMinutes
                => $"a whole number of minutes from 0 (never) to {MaxLockIdleMinutes}",
            KeyboardLayoutKey or LogFileKey or PathKey or PathExtensionsKey or LoginScreenKey or LockIdleKey => null,
            _ when key.StartsWith(FileTypes.AssociationKeyPrefix) => FileTypes.ValidateAssociation(key[FileTypes.AssociationKeyPrefix.Length..], value),
            _ => $"unknown setting '{key}' (known: {string.Join(", ", Known.Select(k => k.Key))}, {FileTypes.AssociationKeyPrefix}<extension>)",
        };
    }

    /// <summary>
    /// Validates, saves and applies a setting of the machine's. An empty value turns a setting off
    /// (no log file, no association, no extra folders or extensions) where that makes sense.
    /// </summary>
    /// <returns>Null on success, else why it failed (also when <paramref name="caller"/> may not change settings).</returns>
    public static string? Set(Caller caller, string key, string value)
    {
        if (caller.DenyUnlessAdmin("change the machine's settings") is { } denied)
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
    /// Sets a machine setting back to its built-in default, written into the file like any value.
    /// A setting without a default (an association the user added) is removed from the file.
    /// </summary>
    /// <returns>Null on success, else why it failed (also when <paramref name="caller"/> may not change settings).</returns>
    public static string? Reset(Caller caller, string key)
    {
        if (caller.DenyUnlessAdmin("change the machine's settings") is { } denied)
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

    /// <summary>
    /// Sets a setting for the caller's own account, over the machine's value: no admin rights
    /// needed, since it changes only the account's own file (written through
    /// <see cref="UserFiles"/>, so the profile's permission rules apply).
    /// </summary>
    /// <returns>Null on success, else why it failed (a machine setting, an invalid value, a file that cannot be written).</returns>
    public static string? SetOwn(Caller caller, string key, string value)
    {
        if (DenyOwn(caller, key) is { } denied)
        {
            return denied;
        }

        if (Validate(key, value) is { } error)
        {
            return error;
        }

        // Changed from what is on disk (a hand edit included), not an older copy saved over it.
        KeyValueFile changed = AccountSettings.Reload(caller.Account).Copy();
        changed.Set(key, value);
        return SaveOwn(caller, changed, key);
    }

    /// <summary>Removes the caller's own value of a setting, so the machine's applies again.</summary>
    /// <returns>Null on success, else why it failed.</returns>
    public static string? ResetOwn(Caller caller, string key)
    {
        if (DenyOwn(caller, key) is { } denied)
        {
            return denied;
        }

        KeyValueFile changed = AccountSettings.Reload(caller.Account).Copy();
        return changed.Remove(key) ? SaveOwn(caller, changed, key) : $"You have not set {key} yourself.";
    }

    /// <summary>
    /// Reads the account's own settings file again, so a hand edit applies, and applies the
    /// settings that take effect by themselves.
    /// </summary>
    /// <returns>The problems in it: settings that are unknown, the machine's only, or invalid (ignored).</returns>
    public static IReadOnlyList<string> ReloadOwn(Account account)
    {
        List<string> problems = [];
        foreach ((string key, string value) in AccountSettings.Reload(account).Entries)
        {
            if (!IsKnown(key))
            {
                problems.Add($"unknown setting '{key}' ignored");
            }
            else if (GetScope(key) == SettingScope.Machine)
            {
                problems.Add($"{key} is a machine setting; ignored here");
            }
            else if (Validate(key, value) is { } error)
            {
                problems.Add($"{key}: {error}; ignored");
            }
        }

        // The same way a change through config applies (SaveOwn), so a new account setting that
        // takes effect by itself needs only its case in Apply.
        foreach ((string key, string _, string _, SettingScope scope) in Known)
        {
            if (scope == SettingScope.Account)
            {
                Apply(key);
            }
        }

        return problems;
    }

    /// <summary>Returns why <paramref name="account"/> has no settings file of its own (the system), or null when it has one.</summary>
    public static string? DenyOwnSettings(Account account)
    {
        return AccountSettings.GetPath(account) is null
            ? $"{account.Name} has no settings of its own: use --system for the machine's."
            : null;
    }

    /// <summary>Returns why the caller cannot set <paramref name="key"/> for itself, or null when it can.</summary>
    private static string? DenyOwn(Caller caller, string key)
    {
        if (!IsKnown(key))
        {
            return Validate(key, string.Empty);
        }

        if (GetScope(key) == SettingScope.Machine)
        {
            return $"{key} is the machine's setting, not one of your own: change it with 'admin config set --system {key} <value>'.";
        }

        return DenyOwnSettings(caller.Account) ?? (CanSave ? null : FileSystemManager.NoDiskMessage);
    }

    private static string? SaveOwn(Caller caller, KeyValueFile changed, string key)
    {
        string path = AccountSettings.GetPath(caller.Account)!;
        try
        {
            UserFiles.SafeWriteAllLines(caller, path, changed.Lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not save {path}: {ex.Message}";
        }

        AccountSettings.Store(caller.Account, changed);
        Apply(key);
        return null;
    }

    /// <summary>
    /// Sets the keyboard's layout: that of the account logged in at the machine, whoever changed
    /// a setting (an account changing its own over Telnet must not change the keyboard of whoever
    /// sits at the machine), else the machine's (at the login screen). There is one keyboard:
    /// Telnet and remote desktop clients send characters their own keyboard already translated.
    /// Runs when a layout setting changes, and when someone logs in or out at the machine.
    /// </summary>
    public static void ApplyKeyboardLayout()
    {
        if (!KeyboardManager.IsEnabled)
        {
            return;
        }

        string value = LogonSessions.AtMachine is { } session
            ? Get(KeyboardLayoutKey, session.Account)
            : Get(KeyboardLayoutKey);

        KeyboardManager.SetLayout(Layouts.TryGetValue(value, out Func<KeyboardLayout>? layout) ? layout() : Layouts[GetDefault(KeyboardLayoutKey)]());
    }

    /// <summary>
    /// Applies a setting that takes effect by itself to the running system, after the machine's
    /// or an account's value changed (the others, such as file associations and <c>path</c>, are
    /// read each time they are needed). For the keyboard layout the machine's value is the login
    /// screen's; the account's at the machine stays over it.
    /// </summary>
    private static void Apply(string key)
    {
        switch (key)
        {
            case KeyboardLayoutKey:
                ApplyKeyboardLayout();
                break;

            case LogFileKey:
                string value = Get(key);
                if (Validate(key, value) is not null)
                {
                    value = GetDefault(key);
                }

                SystemLog.SetFile(value.Length == 0 || !CanSave ? null : value);
                break;
        }
    }

    /// <summary>
    /// Changes the lines and saves them, or reports why it could not. The file is read afresh
    /// first, so the change is made to what is on disk (a hand edit included) rather than to an
    /// older copy saved over it.
    /// </summary>
    private static string? WithFile(Action change)
    {
        if (!CanSave)
        {
            return FileSystemManager.NoDiskMessage;
        }

        KeyValueFile before = file.Copy();
        try
        {
            if (File.Exists(SystemPaths.SettingsFile))
            {
                file = new(File.ReadAllLines(SystemPaths.SettingsFile));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not read {SystemPaths.SettingsFile}: {ex.Message}";
        }

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
