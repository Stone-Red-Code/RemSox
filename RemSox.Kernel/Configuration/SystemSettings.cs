using Cosmos.Kernel.System.Input;
using Cosmos.Kernel.System.Input.Layouts;

using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;
using RemSox.Kernel.Programs;
using RemSox.Shared.Cryptography;
using RemSox.Shared.Networking;

namespace RemSox.Kernel.Configuration;

/// <summary>
/// The settings that survive a reboot, kept in <see cref="SystemPaths.SettingsFile"/>: one
/// <c>key = value</c> per line, <c>#</c> starting a comment. Commands to run at boot go in the
/// startup script instead (<see cref="SystemPaths.StartupScript"/>).
/// </summary>
/// <remarks>
/// Changes rewrite only the lines they touch, so comments and the order of the file are kept.
/// Without a mounted disk the defaults apply and nothing can be saved.
/// </remarks>
public static class SystemSettings
{
    public const string KeyboardLayoutKey = "keyboard.layout";
    public const string LogFileKey = "log.file";
    public const string PathKey = "path";
    public const string PathExtensionsKey = "pathext";
    public const string RdpPasswordKey = "rdp.password";

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

    /// <summary>The settings <c>config</c> knows: name, default, description.</summary>
    public static readonly IReadOnlyList<(string Key, string Default, string Description)> Known =
    [
        (KeyboardLayoutKey, "us", "layout of the keyboard: " + string.Join(", ", Layouts.Keys)),
        (LogFileKey, SystemPaths.DefaultLogFile, "where the system log is kept across reboots; empty for none"),
        (PathKey, SystemPaths.ProgramsFolder, "folders searched after the current one, separated by ;"),
        (PathExtensionsKey, ".rsh;.ynt;.rsl", "extensions tried in order for a name without one, separated by ;"),
        (RdpPasswordKey, string.Empty, "hash of the password remote desktop viewers must give ('config set' hashes what you type); rdp does not start without one. A change applies when rdp next starts"),
    ];

    private static List<string> lines = [];

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
            lines = [];
            return problems;
        }

        try
        {
            bool exists = File.Exists(SystemPaths.SettingsFile);
            // Replaced only once read: a reload that fails keeps the settings in effect.
            lines = exists ? [.. File.ReadAllLines(SystemPaths.SettingsFile)] : [.. FileHeader];

            if (AddMissingLocked() || !exists)
            {
                SaveLocked();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add("could not read or update the file: " + ex.Message);
        }

        foreach ((string key, string value) in GetEntries())
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

        foreach ((string key, string _, string _) in Known)
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

    /// <summary>Returns a setting's value: the last line that sets it, else its default.</summary>
    public static string Get(string key)
    {
        string? value = GetEntries().LastOrDefault(e => e.Key == key).Value;
        return value ?? GetDefault(key);
    }

    /// <summary>
    /// Returns whether <paramref name="key"/> names a setting: a known one, or one of the
    /// <c>open.&lt;extension&gt;</c> file associations, which <see cref="FileTypes"/> owns.
    /// </summary>
    public static bool IsKnown(string key)
    {
        return Known.Any(k => k.Key == key) || key.StartsWith(FileTypes.AssociationKeyPrefix);
    }

    /// <summary>Returns the keys the file sets that start with <paramref name="prefix"/>.</summary>
    public static IEnumerable<string> GetKeys(string prefix)
    {
        return GetEntries().Select(e => e.Key).Where(key => key.StartsWith(prefix)).Distinct();
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
    /// Returns the folders of the <c>path</c> setting, in order. The shell searches the current
    /// folder first, then these.
    /// </summary>
    public static IReadOnlyList<string> GetPathFolders()
    {
        string value = Get(PathKey);
        if (Validate(PathKey, value) is not null)
        {
            value = SystemPaths.ProgramsFolder;
        }

        return value.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Returns whether the file sets <paramref name="key"/>. After <see cref="Load"/> it sets
    /// every setting that has a default, unless there is no disk to keep the file on.
    /// </summary>
    public static bool IsSet(string key)
    {
        return GetEntries().Any(e => e.Key == key);
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
            RdpPasswordKey when value.Length > 0 && PasswordHash.Parse(value) is null => "not a password hash; set the password with 'config set " + RdpPasswordKey + " <password>'",
            KeyboardLayoutKey or LogFileKey or PathKey or PathExtensionsKey or RdpPasswordKey => null,
            _ when key.StartsWith(FileTypes.AssociationKeyPrefix) => FileTypes.ValidateAssociation(key[FileTypes.AssociationKeyPrefix.Length..], value),
            _ => $"unknown setting '{key}' (known: {string.Join(", ", Known.Select(k => k.Key))}, {FileTypes.AssociationKeyPrefix}<extension>)",
        };
    }

    /// <summary>
    /// Validates, saves and applies a setting. An empty value turns a setting off (no log file,
    /// no association, no extra folders or extensions) where that makes sense. A password
    /// (<c>rdp.password</c>) is hashed first, which takes a second or two.
    /// </summary>
    /// <returns>Null on success, else why it failed.</returns>
    public static string? Set(string key, string value)
    {
        // Only the hash is stored: the password cannot be read back from the file.
        if (key == RdpPasswordKey && value.Length > 0)
        {
            value = PasswordHash.Create(value, RpcHandshake.DefaultIterations).ToString();
        }

        if (Validate(key, value) is { } error)
        {
            return error;
        }

        if (WithFile(() =>
        {
            int index = lines.FindIndex(line => ParseLine(line)?.Key == key);
            string line = value.Length > 0 ? $"{key} = {value}" : $"{key} =";

            if (index >= 0)
            {
                lines[index] = line;
                _ = lines.RemoveAll(l => !ReferenceEquals(l, line) && ParseLine(l)?.Key == key);
            }
            else
            {
                lines.Add(line);
            }
        }) is { } saveError)
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
    public static string? Reset(string key)
    {
        if (!IsKnown(key))
        {
            return Validate(key, string.Empty);
        }

        if (GetDefault(key) is { Length: > 0 } defaultValue)
        {
            return Set(key, defaultValue);
        }

        if (WithFile(() => lines.RemoveAll(line => ParseLine(line)?.Key == key)) is { } error)
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

    private static List<(string Key, string Value)> GetEntries()
    {
        return [.. lines.Select(ParseLine).Where(e => e is not null).Select(e => e!.Value)];
    }

    /// <summary>Splits a <c>key = value</c> line; null for blank lines, comments and lines without '='.</summary>
    private static (string Key, string Value)? ParseLine(string line)
    {
        string trimmed = line.Trim();
        int equals = trimmed.IndexOf('=');

        if (trimmed.Length == 0 || trimmed[0] == '#' || equals <= 0)
        {
            return null;
        }

        return (trimmed[..equals].Trim().ToLowerInvariant(), trimmed[(equals + 1)..].Trim());
    }

    /// <summary>Changes the lines and saves them, or reports why it could not.</summary>
    private static string? WithFile(Action change)
    {
        if (!CanSave)
        {
            return FileSystemManager.NoDiskMessage;
        }

        List<string> before = [.. lines];
        change();

        try
        {
            SaveLocked();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lines = before;
            return $"Could not save {SystemPaths.SettingsFile}: {ex.Message}";
        }
    }

    private static void SaveLocked()
    {
        SafeFile.WriteAllLines(SystemPaths.SettingsFile, lines);
    }

    /// <summary>
    /// Appends each setting the file lacks (a new file, or one from before the setting existed)
    /// with its default and a comment saying what it does.
    /// </summary>
    /// <returns>Whether anything was added, so the file needs saving.</returns>
    private static bool AddMissingLocked()
    {
        HashSet<string> present = [.. lines.Select(ParseLine).Where(e => e is not null).Select(e => e!.Value.Key)];
        int count = lines.Count;
        List<string> added = [];

        foreach ((string key, string defaultValue, string description) in Known.Where(k => !present.Contains(k.Key)))
        {
            lines.AddRange(["#", $"# {key}: {description}", $"{key} = {defaultValue}"]);
            added.Add(key);
        }

        List<(string Name, string Opener)> associations = [.. FileTypes.GetDefaultAssociations()
            .Where(association => !present.Contains(FileTypes.AssociationKeyPrefix + association.Name))];

        if (associations.Count > 0 && !present.Any(key => key.StartsWith(FileTypes.AssociationKeyPrefix)))
        {
            lines.AddRange([
                "#",
                $"# {FileTypes.AssociationKeyPrefix}<extension>: the command or program that opens such files when",
                "# their name is typed or they are started from the start menu; empty for none.",
            ]);
        }

        foreach ((string name, string opener) in associations)
        {
            lines.Add($"{FileTypes.AssociationKeyPrefix}{name} = {opener}");
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
        "# back to its default. Commands to run at boot go in " + SystemPaths.StartupScript + ".",
    ];
}
