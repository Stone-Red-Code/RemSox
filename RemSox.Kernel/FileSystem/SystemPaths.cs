namespace RemSox.Kernel.FileSystem;

/// <summary>
/// Every file and folder the system itself reads or writes, in one place, so the disk layout
/// can be seen (and changed) here instead of in each class that uses it. Everything the system
/// owns is lowercase; <see cref="DiskLayout"/> creates the folders at boot.
/// </summary>
public static class SystemPaths
{
    /// <summary>Settings, the account files, the service list, logs and other files the system owns.</summary>
    public const string SystemFolder = "/system";

    /// <summary>The persistent settings, read at boot and by <c>config reload</c>, rewritten by <c>config set</c>.</summary>
    public const string SettingsFile = SystemFolder + "/settings.conf";

    /// <summary>The accounts, one <c>id:name:groups</c> line each. Public: any program may list them.</summary>
    public const string AccountsFile = SystemFolder + "/accounts";

    /// <summary>
    /// The accounts' password hashes, one <c>id:hash</c> line each. Kept apart from
    /// <see cref="AccountsFile"/> so the permission system can lock away only the secrets.
    /// </summary>
    public const string PasswordsFile = SystemFolder + "/passwords";

    /// <summary>The services that start at boot, one per line with its arguments (the <c>service</c> command).</summary>
    public const string ServicesFile = SystemFolder + "/services";

    /// <summary>The system log, and its older copy once it grows too big.</summary>
    public const string LogsFolder = SystemFolder + "/logs";

    /// <summary>Where the system log is kept unless the <c>log.file</c> setting says otherwise.</summary>
    public const string DefaultLogFile = LogsFolder + "/remsox.log";

    /// <summary>One <c>.rsl</c> shortcut per start menu entry, read each time the start menu opens.</summary>
    public const string StartMenuFolder = SystemFolder + "/startmenu";

    /// <summary>One folder per account (<c>/home/&lt;name&gt;</c>), where its shells start.</summary>
    public const string HomeFolder = "/home";

    /// <summary>
    /// Programs every user can run by name from anywhere: the default <c>path</c> setting. At the
    /// top level rather than in <see cref="HomeFolder"/>, which is to hold only user folders.
    /// </summary>
    public const string ProgramsFolder = "/programs";

    /// <summary>Scratch space for anything; emptied at every boot.</summary>
    public const string TempFolder = "/temp";
}
