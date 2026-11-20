namespace RemSox.Kernel.FileSystem;

/// <summary>
/// Every file and folder the system itself reads or writes, in one place, so the disk layout
/// can be seen (and changed) here instead of in each class that uses it. Everything the system
/// owns is lowercase; <see cref="DiskLayout"/> creates the folders at boot.
/// </summary>
public static class SystemPaths
{
    /// <summary>Settings, the startup script, logs and other files the system owns.</summary>
    public const string SystemFolder = "/system";

    /// <summary>The persistent settings, read at boot and by <c>config reload</c>, rewritten by <c>config set</c>.</summary>
    public const string SettingsFile = SystemFolder + "/settings.conf";

    /// <summary>The shell script run at boot.</summary>
    public const string StartupScript = SystemFolder + "/startup.rsh";

    /// <summary>The system log, and its older copy once it grows too big.</summary>
    public const string LogsFolder = SystemFolder + "/logs";

    /// <summary>Where the system log is kept unless the <c>log.file</c> setting says otherwise.</summary>
    public const string DefaultLogFile = LogsFolder + "/remsox.log";

    /// <summary>One <c>.rsl</c> shortcut per start menu entry, read each time the start menu opens.</summary>
    public const string StartMenuFolder = SystemFolder + "/startmenu";

    /// <summary>The user's own files; new shells start here and <c>cd</c> alone returns here.</summary>
    public const string HomeFolder = "/home";

    /// <summary>
    /// Programs every user can run by name from anywhere: the default <c>path</c> setting. At the
    /// top level rather than in <see cref="HomeFolder"/>, which is to hold only user folders.
    /// </summary>
    public const string ProgramsFolder = "/programs";

    /// <summary>Scratch space for anything; emptied at every boot.</summary>
    public const string TempFolder = "/temp";
}
