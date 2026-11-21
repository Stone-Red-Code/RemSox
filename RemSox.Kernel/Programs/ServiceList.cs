using RemSox.Kernel.Accounts;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Programs;

/// <summary>
/// The services that start at boot, kept in <see cref="SystemPaths.ServicesFile"/>: one per
/// line, its name and the arguments it starts with (<c>rdp 9999</c>), as typed in a shell.
/// Changed with the <c>service</c> command, like systemd units or Windows services set to
/// start automatically.
/// </summary>
/// <remarks>
/// Services start in the system's session (<see cref="LogonSessions.System"/>), before anyone has
/// logged in, so they keep running whoever logs in or out. Only programs of the
/// <see cref="ProgramKind.Service"/> kind can be listed.
/// </remarks>
public static class ServiceList
{
    private static readonly List<(string Name, string[] Arguments)> entries = [];

    private static readonly string[] FileHeader =
    [
        "# RemSox services: started at boot, as the system, before anyone logs in.",
        "# One per line: the service and its arguments. Change them with the 'service'",
        "# command (service enable rdp 9999, service disable telnet) or by editing this file.",
    ];

    /// <summary>Gets the services that start at boot, in order, with their arguments.</summary>
    public static IReadOnlyList<(string Name, string[] Arguments)> Enabled => entries;

    /// <summary>Reads the list. A line naming no service is logged and skipped.</summary>
    public static void Load()
    {
        entries.Clear();

        if (!FileSystemManager.IsMounted || !File.Exists(SystemPaths.ServicesFile))
        {
            return;
        }

        try
        {
            foreach (string line in ShellScript.ReadCommands(SystemPaths.ServicesFile))
            {
                List<string> words = CommandLine.SplitWords(line);
                if (ValidateName(words[0]) is { } error)
                {
                    SystemLog.Log($"{SystemPaths.ServicesFile}: {error}; ignored", LogSeverity.Warning);
                    continue;
                }

                SetEntry(words[0], [.. words.Skip(1)]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SystemLog.Log($"Could not read {SystemPaths.ServicesFile}: {ex.Message}", LogSeverity.Error);
        }
    }

    /// <summary>Returns the arguments <paramref name="name"/> starts with at boot, or null when it does not start at boot.</summary>
    public static string[]? GetArguments(string name)
    {
        int index = IndexOf(name);
        return index < 0 ? null : entries[index].Arguments;
    }

    /// <summary>Makes <paramref name="name"/> start at boot with <paramref name="arguments"/>, replacing its arguments if it already does.</summary>
    /// <returns>Null, or why it could not be saved.</returns>
    public static string? Enable(string name, string[] arguments)
    {
        if (ValidateName(name) is { } error)
        {
            return error;
        }

        List<(string Name, string[] Arguments)> before = [.. entries];
        SetEntry(name, arguments);
        return SaveOrRestore(before);
    }

    /// <summary>Stops <paramref name="name"/> from starting at boot.</summary>
    /// <returns>Null, or why it could not be saved.</returns>
    public static string? Disable(string name)
    {
        int index = IndexOf(name);
        if (index < 0)
        {
            return $"{name} does not start at boot.";
        }

        List<(string Name, string[] Arguments)> before = [.. entries];
        entries.RemoveAt(index);
        return SaveOrRestore(before);
    }

    /// <summary>Starts every listed service in the system's session; called once at boot.</summary>
    /// <returns>Why each one that failed did not start.</returns>
    public static List<string> StartAll()
    {
        List<string> errors = [];

        foreach ((string name, string[] arguments) in entries)
        {
            ProgramStart start = ProgramRegistry.Find(name) is { } program
                ? ProgramRegistry.Start(program, arguments, FileSystemManager.MountPoint, Caller.System)
                : ProgramStart.Fail("No such program.");

            if (start.Error is not null)
            {
                string error = $"Service {name} did not start: {start.Error}";
                SystemLog.Log(error, LogSeverity.Error);
                errors.Add(error);
            }
        }

        return errors;
    }

    /// <summary>Returns why <paramref name="name"/> cannot be listed (no program, or no service), or null.</summary>
    private static string? ValidateName(string name)
    {
        return ProgramRegistry.Find(name) switch
        {
            null => $"there is no program called {name}",
            { Kind: not ProgramKind.Service } program => $"{program.Name} is no service",
            _ => null,
        };
    }

    private static int IndexOf(string name)
    {
        return entries.FindIndex(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static void SetEntry(string name, string[] arguments)
    {
        string canonical = ProgramRegistry.Find(name)?.Name ?? name;
        int index = IndexOf(canonical);
        if (index >= 0)
        {
            entries[index] = (canonical, arguments);
        }
        else
        {
            entries.Add((canonical, arguments));
        }
    }

    private static string? SaveOrRestore(List<(string Name, string[] Arguments)> before)
    {
        if (!FileSystemManager.IsMounted)
        {
            entries.Clear();
            entries.AddRange(before);
            return FileSystemManager.NoDiskMessage;
        }

        try
        {
            Save();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            entries.Clear();
            entries.AddRange(before);
            return $"Could not save {SystemPaths.ServicesFile}: {ex.Message}";
        }
    }

    private static void Save()
    {
        SafeFile.WriteAllLines(SystemPaths.ServicesFile, [
            .. FileHeader,
            .. entries.Select(entry => string.Join(' ', [entry.Name, .. entry.Arguments.Select(CommandLine.Quote)])),
        ]);
    }
}
