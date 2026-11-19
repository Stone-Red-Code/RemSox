using RemSox.Kernel.Logging;
using RemSox.Kernel.Programs;

namespace RemSox.Kernel.FileSystem;

/// <summary>
/// Puts the mounted disk into the layout <see cref="SystemPaths"/> describes, at every boot rather
/// than only after formatting, so a disk gains whatever folders a newer RemSox adds.
/// </summary>
public static class DiskLayout
{
    private static readonly string[] Folders =
    [
        SystemPaths.SystemFolder,
        SystemPaths.LogsFolder,
        SystemPaths.StartMenuFolder,
        SystemPaths.ProgramsFolder,
        SystemPaths.HomeFolder,
        SystemPaths.TempFolder,
    ];

    /// <summary>
    /// Creates the system folders (a new start menu folder gets the default shortcuts), repairs
    /// system files a crash left mid-write (<see cref="SafeFile.Recover"/>) and empties
    /// <see cref="SystemPaths.TempFolder"/>. Each part that fails is logged and skipped, so a
    /// damaged disk still boots.
    /// </summary>
    public static void Prepare()
    {
        // Written only into a new folder, so a shortcut the user deleted stays deleted.
        bool newStartMenu = !Directory.Exists(SystemPaths.StartMenuFolder);

        foreach (string folder in Folders)
        {
            Try("create " + folder, () => _ = Directory.CreateDirectory(folder));
        }

        if (newStartMenu)
        {
            Try("write the default start menu", Shortcut.WriteDefaults);
        }

        foreach (string file in (string[])[SystemPaths.SettingsFile, SystemPaths.StartupScript])
        {
            Try("recover " + file, () =>
            {
                if (SafeFile.Recover(file))
                {
                    SystemLog.Log($"Restored {file} from the copy an interrupted save left behind.", LogSeverity.Warning);
                }
            });
        }

        Try("empty " + SystemPaths.TempFolder, ClearTemp);
    }

    private static void ClearTemp()
    {
        foreach (string file in Directory.EnumerateFiles(SystemPaths.TempFolder))
        {
            File.Delete(file);
        }

        foreach (string folder in Directory.EnumerateDirectories(SystemPaths.TempFolder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void Try(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SystemLog.Log($"Could not {what}: {ex.Message}", LogSeverity.Error);
        }
    }
}
