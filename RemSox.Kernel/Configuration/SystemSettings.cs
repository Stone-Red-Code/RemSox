using Cosmos.Kernel.System.Input;
using Cosmos.Kernel.System.Input.Layouts;

using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;
using RemSox.Kernel.Programs;

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
        (KeyboardLayoutKey, "us", "keyboard layout: " + string.Join(", ", Layouts.Keys)),
        (LogFileKey, SystemPaths.DefaultLogFile, "where the system log is kept across reboots, or off"),
        (PathKey, SystemPaths.ProgramsFolder, "folders searched for files typed by name, separated by ;"),
        (PathExtensionsKey, ".rsh;.ynt;.rsl", "extensions tried in order for a name typed without one, separated by ;"),
    ];

    private static readonly Lock fileLock = new();
    private static List<string> lines = [];

    /// <summary>Gets whether settings can be saved (a disk is mounted).</summary>
    public static bool CanSave => FileSystemManager.IsMounted;

    /// <summary>
    /// Reads the settings file, creating it with commented defaults when it is missing, and
    /// applies the settings. Problems are logged and leave the defaults in place.
    /// </summary>
    public static void Load()
    {
        lock (fileLock)
        {
            lines = [];

            if (!CanSave)
            {
                return;
            }

            try
            {
                if (File.Exists(SystemPaths.SettingsFile))
                {
                    lines = [.. File.ReadAllLines(SystemPaths.SettingsFile)];
                }
                else
                {
                    lines = [.. DefaultFile];
                    SaveLocked();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SystemLog.Log("Could not read " + SystemPaths.SettingsFile + ": " + ex.Message, LogSeverity.Error);
            }
        }

        foreach ((string key, string value) in GetEntries())
        {
            if (!IsKnown(key))
            {
                SystemLog.Log($"{SystemPaths.SettingsFile}: unknown setting '{key}' ignored", LogSeverity.Warning);
            }
            else if (!Known.Any(k => k.Key == key) && Validate(key, value) is { } error)
            {
                SystemLog.Log($"{SystemPaths.SettingsFile}: {key}: {error}; ignored", LogSeverity.Warning);
            }
        }

        foreach ((string key, string _, string _) in Known)
        {
            if (Validate(key, Get(key)) is { } error)
            {
                SystemLog.Log($"{SystemPaths.SettingsFile}: {key}: {error}; using the default", LogSeverity.Warning);
            }

            Apply(key);
        }
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

    /// <summary>Returns whether the file sets <paramref name="key"/> (rather than using the default).</summary>
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
            LogFileKey when value != "off" && !value.StartsWith('/') => "an absolute path, or off",
            PathKey when value.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(folder => !folder.StartsWith('/'))
                => "absolute folders separated by " + PathSeparator,
            PathExtensionsKey when value.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is var extensions
                && (extensions.Length == 0 || extensions.Any(extension => extension.Length < 2 || extension[0] != '.' || extension.IndexOf('.', 1) >= 0))
                => "extensions such as .rsh, separated by " + PathSeparator,
            KeyboardLayoutKey or LogFileKey or PathKey or PathExtensionsKey => null,
            _ when key.StartsWith(FileTypes.AssociationKeyPrefix) => FileTypes.ValidateAssociation(key[FileTypes.AssociationKeyPrefix.Length..], value),
            _ => $"unknown setting '{key}' (known: {string.Join(", ", Known.Select(k => k.Key))}, {FileTypes.AssociationKeyPrefix}<extension>)",
        };
    }

    /// <summary>Validates, saves and applies a setting.</summary>
    /// <returns>Null on success, else why it failed.</returns>
    public static string? Set(string key, string value)
    {
        if (Validate(key, value) is { } error)
        {
            return error;
        }

        if (WithFile(() =>
        {
            int index = lines.FindIndex(line => ParseLine(line)?.Key == key);
            string line = $"{key} = {value}";

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

    /// <summary>Removes a setting from the file, so its default applies again.</summary>
    public static string? Reset(string key)
    {
        if (!IsKnown(key))
        {
            return Validate(key, string.Empty);
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
                SystemLog.SetFile(value == "off" || !CanSave ? null : value);
                break;
        }
    }

    private static List<(string Key, string Value)> GetEntries()
    {
        lock (fileLock)
        {
            return [.. lines.Select(ParseLine).Where(e => e is not null).Select(e => e!.Value)];
        }
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

        lock (fileLock)
        {
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
    }

    private static void SaveLocked()
    {
        SafeFile.WriteAllLines(SystemPaths.SettingsFile, lines);
    }

    private static readonly string[] DefaultFile =
    [
        "# RemSox settings, read at boot. Change them with the 'config' command,",
        "# or edit this file. '#' starts a comment.",
        "#",
        "# keyboard.layout: " + string.Join(", ", Layouts.Keys),
        "keyboard.layout = us",
        "#",
        "# log.file: where the system log is kept across reboots, or off",
        "log.file = " + SystemPaths.DefaultLogFile,
        "#",
        "# path: folders searched for files typed by name, after the current folder, separated by ;",
        "path = " + SystemPaths.ProgramsFolder,
        "#",
        "# pathext: extensions tried, in order, when a name is typed without one",
        "pathext = .rsh;.ynt;.rsl",
        "#",
        "# open.<extension>: the command or program that opens such files when their name is",
        "# typed or they are started from the start menu, or none. 'config list' shows them all.",
        "# open.txt = edit",
        "#",
        "# Commands to run at boot go in the startup script, " + SystemPaths.StartupScript + ".",
    ];
}
