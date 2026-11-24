namespace RemSox.Kernel.FileSystem;

/// <summary>
/// Every file and folder the system itself reads or writes, in one place, so the disk layout
/// can be seen (and changed) here instead of in each class that uses it. Everything the system
/// owns is lowercase; <see cref="DiskLayout"/> creates the folders at boot.
/// </summary>
/// <remarks>
/// <code>
/// /system/            the machine
///     config/         settings, services, accounts, groups, permissions: public
///     secrets/        password hashes: for the system only, once permissions exist
///     logs/
///     startmenu/      shortcuts for everyone
/// /programs/          programs for everyone
/// /users/&lt;name&gt;/     each account's profile (see Accounts.Account)
///     home/           the account's own files; its shells start here
///     config/         what the system keeps for it: its startup script, startmenu/
///     programs/       its own programs, searched before /programs
/// /temp/              scratch, emptied at boot
/// </code>
/// Folders are grouped by who may read them, so the permission system can protect whole folders.
/// </remarks>
public static class SystemPaths
{
    /// <summary>Everything the machine itself owns: configuration, secrets, logs, the start menu.</summary>
    public const string SystemFolder = "/system";

    /// <summary>The machine's configuration, which any program may read.</summary>
    public const string ConfigFolder = SystemFolder + "/config";

    /// <summary>What only the system may read once permissions exist: the password hashes.</summary>
    public const string SecretsFolder = SystemFolder + "/secrets";

    /// <summary>The persistent settings, read at boot and by <c>config reload</c>, rewritten by <c>config set</c>.</summary>
    public const string SettingsFile = ConfigFolder + "/settings.conf";

    /// <summary>The accounts, one <c>id:name:group ids</c> line each. Public: any program may list them.</summary>
    public const string AccountsFile = ConfigFolder + "/accounts";

    /// <summary>The groups administrators made, one <c>id:name</c> line each. Public.</summary>
    public const string GroupsFile = ConfigFolder + "/groups";

    /// <summary>
    /// The permission rules administrators and owners add (<c>perm</c>). Public, so anyone can see
    /// why they may or may not do something.
    /// </summary>
    public const string PermissionsFile = ConfigFolder + "/permissions";

    /// <summary>
    /// The accounts' password hashes, one <c>id:hash</c> line each. Kept apart from
    /// <see cref="AccountsFile"/>, in <see cref="SecretsFolder"/>, so the permission system can
    /// lock away only the secrets.
    /// </summary>
    public const string PasswordsFile = SecretsFolder + "/passwords";

    /// <summary>The services that start at boot, one per line with its arguments (the <c>service</c> command).</summary>
    public const string ServicesFile = ConfigFolder + "/services";

    /// <summary>The system log, and its older copy once it grows too big.</summary>
    public const string LogsFolder = SystemFolder + "/logs";

    /// <summary>Where the system log is kept unless the <c>log.file</c> setting says otherwise.</summary>
    public const string DefaultLogFile = LogsFolder + "/remsox.log";

    /// <summary>
    /// The start menu entries everyone sees, one <c>.rsl</c> shortcut each, read each time a start
    /// menu opens; each account adds its own (<see cref="Accounts.Account.StartMenuFolder"/>).
    /// </summary>
    public const string StartMenuFolder = SystemFolder + "/startmenu";

    /// <summary>
    /// The pointer images, one <c>.cur</c> file per pointer role (<see cref="UI.GUI.Cursors"/>):
    /// each is written from the copy built into the kernel when it is missing, and replacing one
    /// changes that pointer from the next boot.
    /// </summary>
    public const string CursorsFolder = SystemFolder + "/cursors";

    /// <summary>
    /// The background images, <c>desktop</c> and <c>lock</c> (<c>.qoi</c> or <c>.bmp</c>,
    /// <see cref="UI.GUI.Wallpapers"/>): each is written from the copy built into the kernel when
    /// missing, and replacing one changes it for everyone without their own.
    /// </summary>
    public const string WallpapersFolder = SystemFolder + "/wallpapers";

    /// <summary>One profile folder per account, <c>/users/&lt;name&gt;</c>.</summary>
    public const string UsersFolder = "/users";

    /// <summary>Programs every user can run by name from anywhere: the default <c>path</c> setting.</summary>
    public const string ProgramsFolder = "/programs";

    /// <summary>Scratch space for anything; emptied at every boot.</summary>
    public const string TempFolder = "/temp";
}
