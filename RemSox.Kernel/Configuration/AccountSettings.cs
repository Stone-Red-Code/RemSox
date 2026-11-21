using RemSox.Kernel.Accounts;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;

namespace RemSox.Kernel.Configuration;

/// <summary>
/// Each account's own settings, <c>/users/&lt;name&gt;/config/settings.conf</c>: only what the
/// account changed, layered over the machine's (see <see cref="SystemSettings.Get(string, Account)"/>).
/// Read once per account and kept in memory, so resolving a setting does not read the disk; a
/// change through <c>config</c> updates the copy, a hand edit applies after <c>config reload</c>.
/// </summary>
/// <remarks>
/// The system reads these files directly (the kernel is trusted); writes go through
/// <see cref="Security.UserFiles"/> with the caller, so the profile's permission rules decide who
/// may change them.
/// </remarks>
public static class AccountSettings
{
    public const string FileName = "settings.conf";

    private static readonly Dictionary<int, KeyValueFile> files = [];

    /// <summary>Returns the path of the account's settings file, or null for an account without a profile (the system).</summary>
    public static string? GetPath(Account account)
    {
        return account.ConfigFolder is { } folder ? folder + "/" + FileName : null;
    }

    /// <summary>Returns the account's settings, read from its file the first time; empty when it has none.</summary>
    public static KeyValueFile Get(Account account)
    {
        if (files.TryGetValue(account.Id, out KeyValueFile? file))
        {
            return file;
        }

        file = Read(account);
        files[account.Id] = file;
        return file;
    }

    /// <summary>Replaces the copy in memory, after the file was saved.</summary>
    internal static void Store(Account account, KeyValueFile file)
    {
        files[account.Id] = file;
    }

    /// <summary>Forgets the copy in memory, so the file is read again at the next use.</summary>
    public static void Forget(Account account)
    {
        _ = files.Remove(account.Id);
    }

    private static KeyValueFile Read(Account account)
    {
        if (!FileSystemManager.IsMounted || GetPath(account) is not { } path || !File.Exists(path))
        {
            return new([]);
        }

        try
        {
            return new(File.ReadAllLines(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SystemLog.Log($"Could not read {path}: {ex.Message}", LogSeverity.Warning);
            return new([]);
        }
    }
}
