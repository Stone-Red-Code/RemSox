using System.Globalization;

using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;
using RemSox.Kernel.Processing;
using RemSox.Shared.Cryptography;
using RemSox.Shared.Networking;

namespace RemSox.Kernel.Accounts;

/// <summary>
/// The accounts and their passwords, kept in <see cref="SystemPaths.AccountsFile"/> (public) and
/// <see cref="SystemPaths.PasswordsFile"/> (secret), as Linux keeps <c>/etc/passwd</c> and
/// <c>/etc/shadow</c>. Every change rewrites the files at once.
/// </summary>
/// <remarks>
/// Without a disk there are no accounts and none can be made, so nobody can log in. Passwords
/// are stored as <see cref="PasswordHash"/>es; hashing or checking one takes a second or two in
/// QEMU, so it runs on a thread of its own (<see cref="BackgroundWork"/>) and the methods that do
/// it are asynchronous: the system keeps running meanwhile.
/// </remarks>
public static class AccountStore
{
    /// <summary>The longest account name.</summary>
    public const int MaxNameLength = 32;

    private static readonly List<Account> accounts = [];
    private static readonly Dictionary<int, PasswordHash> passwords = [];

    // The highest id ever given, so a deleted account's id is never given again. The accounts
    // file keeps it in a "#highest id:" line, which survives deleting the newest account.
    private const string HighestIdPrefix = "#highest id:";
    private static int highestId = Account.FirstUserId - 1;

    /// <summary>Gets whether accounts can exist: a disk is mounted to keep them on.</summary>
    public static bool IsAvailable => FileSystemManager.IsMounted;

    /// <summary>Gets the accounts, in the order they were created.</summary>
    public static IReadOnlyList<Account> All => accounts;

    /// <summary>Gets whether any account exists; until one does, only the console can create it.</summary>
    public static bool HasAccounts => accounts.Count > 0;

    /// <summary>
    /// Reads both files. Lines that cannot be read are logged and skipped; an account without a
    /// password cannot log in until an admin sets one.
    /// </summary>
    public static void Load()
    {
        GroupStore.Load();
        accounts.Clear();
        passwords.Clear();
        highestId = Account.FirstUserId - 1;

        if (!IsAvailable)
        {
            return;
        }

        try
        {
            if (File.Exists(SystemPaths.AccountsFile))
            {
                foreach (string line in File.ReadAllLines(SystemPaths.AccountsFile))
                {
                    ReadAccountLine(line);
                }
            }

            if (File.Exists(SystemPaths.PasswordsFile))
            {
                foreach (string line in File.ReadAllLines(SystemPaths.PasswordsFile))
                {
                    ReadPasswordLine(line);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SystemLog.Log("Could not read the accounts: " + ex.Message, LogSeverity.Error);
        }
    }

    /// <summary>Returns the account called <paramref name="name"/>, ignoring case, or null.</summary>
    public static Account? Find(string name)
    {
        return accounts.Find(account => string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns the account with <paramref name="id"/>, or null.</summary>
    public static Account? Find(int id)
    {
        return id == Account.SystemId ? Account.System : accounts.Find(account => account.Id == id);
    }

    /// <summary>Returns why <paramref name="name"/> cannot name a new account, or null when it can.</summary>
    public static string? ValidateName(string name)
    {
        if (!IsWellFormedName(name))
        {
            return $"An account name is 1-{MaxNameLength} characters a-z, 0-9, _ or -, starting with a letter.";
        }

        if (Find(name) is not null || string.Equals(name, Account.System.Name, StringComparison.OrdinalIgnoreCase))
        {
            return $"There is already an account called {name}.";
        }

        return null;
    }

    /// <summary>Returns why <paramref name="password"/> cannot be used, or null when it can.</summary>
    public static string? ValidatePassword(string password)
    {
        return password.Length == 0 ? "The password cannot be empty." : null;
    }

    /// <summary>
    /// Creates an account with its home folder. The first account is always an admin, so the
    /// system can be managed. The password is hashed in the background first.
    /// </summary>
    /// <returns>The account, or why it could not be created.</returns>
    public static async Task<(Account? Account, string? Error)> CreateAsync(string name, string password, bool admin)
    {
        MainLoop.AssertMainThread(nameof(CreateAsync));

        name = name.ToLowerInvariant();
        if (!IsAvailable)
        {
            return (null, FileSystemManager.NoDiskMessage);
        }

        if ((ValidateName(name) ?? ValidatePassword(password)) is { } invalid)
        {
            return (null, invalid);
        }

        PasswordHash hash = await HashAsync(password);

        // Checked again: another account of that name may have been made meanwhile.
        if (!IsAvailable)
        {
            return (null, FileSystemManager.NoDiskMessage);
        }

        if (ValidateName(name) is { } error)
        {
            return (null, error);
        }

        Account account = new(highestId + 1, name, admin || !HasAccounts ? [Group.AdminsId] : []);

        // A profile folder already there belonged to an account deleted before profiles were moved
        // aside, or whose move failed: the new account must not take over its files and rules.
        if (Directory.Exists(account.ProfileFolder) && MoveProfileAside(account.ProfileFolder!).Error is { } profileError)
        {
            return (null, profileError);
        }

        // The password first: an account whose password failed to save cannot be logged in to,
        // while a password with no account is only a stray line.
        passwords[account.Id] = hash;
        accounts.Add(account);
        if (Save() is { } saveError)
        {
            _ = accounts.Remove(account);
            _ = passwords.Remove(account.Id);
            return (null, saveError);
        }

        highestId = account.Id;
        EnsureProfile(account);
        Programs.UserStartup.WriteDefault(account);
        SystemLog.Log($"Created account {account.Name} (id {account.Id}{(account.IsAdmin ? ", admin" : string.Empty)}).", LogSeverity.Info);
        return (account, null);
    }

    /// <summary>
    /// Deletes an account. Its profile folder is kept, renamed to <c>/users/&lt;name&gt;.deleted</c>
    /// (<see cref="MoveProfileAside"/>), and its id is never given again. The last admin cannot
    /// be deleted, so the system can always be managed.
    /// </summary>
    /// <param name="account">The account to delete.</param>
    /// <param name="keptProfile">Where its profile is now; null when it had none or could not be moved (logged).</param>
    /// <returns>Null, or why it could not be deleted.</returns>
    public static string? Delete(Account account, out string? keptProfile)
    {
        MainLoop.AssertMainThread(nameof(Delete));
        keptProfile = null;

        if (account.IsAdmin && accounts.Count(a => a.IsAdmin) == 1)
        {
            return $"{account.Name} is the last administrator and cannot be deleted.";
        }

        int index = accounts.IndexOf(account);
        if (index < 0)
        {
            return $"There is no account called {account.Name}.";
        }

        PasswordHash? hash = passwords.GetValueOrDefault(account.Id);
        accounts.RemoveAt(index);
        _ = passwords.Remove(account.Id);

        if (Save() is { } error)
        {
            accounts.Insert(index, account);
            if (hash is not null)
            {
                passwords[account.Id] = hash;
            }

            return error;
        }

        SystemLog.Log($"Deleted account {account.Name} (id {account.Id}).", LogSeverity.Info);

        // A failure is only logged: the account is gone either way, and Create moves the folder
        // aside before an account of the same name could take it over.
        if (account.ProfileFolder is { } profile && Directory.Exists(profile))
        {
            (keptProfile, string? profileError) = MoveProfileAside(profile);
            if (profileError is not null)
            {
                SystemLog.Log(profileError, LogSeverity.Error);
            }
        }

        return null;
    }

    /// <summary>
    /// Renames a profile no account owns any more to <c>&lt;name&gt;.deleted</c> (or
    /// <c>.deleted2</c>, ... when taken), with the permission rules on it. A profile belongs to
    /// the account of its folder's name, and a dot is in no account name, so whoever is later
    /// given the same name starts with an empty profile; administrators can still reach the old one.
    /// </summary>
    /// <returns>
    /// Where the profile is now (null when it could not be moved), and why something failed: the
    /// move, or moving its rules (the folder is moved then, but the rules still name the old path).
    /// </returns>
    private static (string? Aside, string? Error) MoveProfileAside(string profile)
    {
        string aside = profile + ".deleted";
        for (int i = 2; Directory.Exists(aside) || File.Exists(aside); i++)
        {
            aside = profile + ".deleted" + i.ToString(CultureInfo.InvariantCulture);
        }

        try
        {
            Directory.Move(profile, aside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"Could not move the old profile {profile} aside: {ex.Message}");
        }

        SystemLog.Log($"Moved the profile {profile}, which no account owns, to {aside}.", LogSeverity.Info);
        return (aside, Security.PermissionRules.MoveFolder(profile, aside));
    }

    /// <summary>
    /// Puts <paramref name="account"/> in exactly the groups <paramref name="groupIds"/> lists. The
    /// last administrator cannot leave <see cref="Group.Admins"/>, so the system can always be
    /// managed. Sessions already logged in keep the groups they logged in with, as on Linux: the
    /// change applies from the account's next login.
    /// </summary>
    /// <returns>The changed account, or why it could not be changed.</returns>
    public static (Account? Account, string? Error) SetGroups(Account account, IReadOnlyList<int> groupIds)
    {
        MainLoop.AssertMainThread(nameof(SetGroups));

        int index = accounts.FindIndex(a => a.Id == account.Id);
        if (index < 0)
        {
            return (null, $"There is no account called {account.Name}.");
        }

        Account old = accounts[index];
        Account changed = new(old.Id, old.Name, [.. groupIds.Distinct()]);
        if (old.IsAdmin && !changed.IsAdmin && accounts.Count(a => a.IsAdmin) == 1)
        {
            return (null, $"{old.Name} is the last administrator and cannot leave {Group.Admins.Name}.");
        }

        accounts[index] = changed;
        if (Save() is { } error)
        {
            accounts[index] = old;
            return (null, error);
        }

        return (changed, null);
    }

    /// <summary>Takes every account out of the group with <paramref name="groupId"/>; for <see cref="GroupStore.Delete"/>.</summary>
    /// <returns>Null, or why the accounts could not be saved.</returns>
    internal static string? RemoveFromGroup(int groupId)
    {
        List<Account> before = [.. accounts];
        for (int i = 0; i < accounts.Count; i++)
        {
            if (accounts[i].IsInGroup(groupId))
            {
                accounts[i] = new Account(accounts[i].Id, accounts[i].Name, [.. accounts[i].GroupIds.Where(id => id != groupId)]);
            }
        }

        if (Save() is { } error)
        {
            accounts.Clear();
            accounts.AddRange(before);
            return error;
        }

        return null;
    }

    /// <summary>Sets an account's password, hashed in the background first.</summary>
    /// <returns>Null, or why it could not be set.</returns>
    public static async Task<string?> SetPasswordAsync(Account account, string password)
    {
        MainLoop.AssertMainThread(nameof(SetPasswordAsync));

        if (ValidatePassword(password) is { } error)
        {
            return error;
        }

        PasswordHash hash = await HashAsync(password);
        if (Find(account.Id) != account)
        {
            return $"There is no account called {account.Name} any more.";
        }

        return StorePassword(account, hash);
    }

    /// <summary>Hashes a password in the background, at today's iteration count.</summary>
    private static Task<PasswordHash> HashAsync(string password)
    {
        return BackgroundWork.Run(() => PasswordHash.Create(password, RpcHandshake.DefaultIterations));
    }

    /// <summary>Replaces an account's password hash and saves it, keeping the old one when saving fails.</summary>
    private static string? StorePassword(Account account, PasswordHash hash)
    {
        PasswordHash? old = passwords.GetValueOrDefault(account.Id);
        passwords[account.Id] = hash;

        if (Save() is { } saveError)
        {
            if (old is null)
            {
                _ = passwords.Remove(account.Id);
            }
            else
            {
                passwords[account.Id] = old;
            }

            return saveError;
        }

        SystemLog.Log($"Changed the password of {account.Name}.", LogSeverity.Info);
        return null;
    }

    /// <summary>
    /// Checks a name and password, in the background. Slow (a second or two in QEMU) whether or
    /// not the account exists, so the time taken does not tell which names exist. A password
    /// hashed with fewer rounds than today's default is hashed again, so old hashes get stronger
    /// over time.
    /// </summary>
    /// <returns>The account, or null when the name or the password is wrong.</returns>
    public static async Task<Account?> VerifyAsync(string name, string password)
    {
        MainLoop.AssertMainThread(nameof(VerifyAsync));

        Account? account = Find(name);
        PasswordHash? hash = account is null ? null : passwords.GetValueOrDefault(account.Id);

        if (account is null || hash is null)
        {
            // Spend the same time as a real check.
            _ = await BackgroundWork.Run(() => PasswordHash.Create(password, RpcHandshake.DefaultIterations));
            return null;
        }

        if (!await BackgroundWork.Run(() => hash.Matches(password)))
        {
            return null;
        }

        // Deleted, or its password changed, while the check ran: the password checked is no longer its.
        if (Find(account.Id) != account || passwords.GetValueOrDefault(account.Id) != hash)
        {
            return null;
        }

        if (hash.Iterations < RpcHandshake.DefaultIterations)
        {
            _ = await SetPasswordAsync(account, password);
        }

        return account;
    }

    /// <summary>Returns the account's password hash, or null when it has none.</summary>
    public static PasswordHash? GetPasswordHash(Account account)
    {
        return passwords.GetValueOrDefault(account.Id);
    }

    /// <summary>
    /// Creates the folders of the account's profile that are missing (home, config, its start
    /// menu, programs),
    /// so a profile someone deleted parts of works again at the next login; failures are logged.
    /// </summary>
    public static void EnsureProfile(Account account)
    {
        if (account.IsSystem || !IsAvailable)
        {
            return;
        }

        foreach (string folder in (string[])[account.HomeFolder, account.ConfigFolder!, account.StartMenuFolder!, account.ProgramsFolder!])
        {
            try
            {
                _ = Directory.CreateDirectory(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SystemLog.Log($"Could not create {folder}: {ex.Message}", LogSeverity.Error);
            }
        }
    }

    private static string? Save()
    {
        try
        {
            SafeFile.WriteAllLines(SystemPaths.PasswordsFile, [
                "# RemSox passwords: id:hash, one per account. Secret; change them with 'user password'.",
                .. accounts.Where(a => passwords.ContainsKey(a.Id))
                    .Select(a => $"{a.Id.ToString(CultureInfo.InvariantCulture)}:{passwords[a.Id]}"),
            ]);

            SafeFile.WriteAllLines(SystemPaths.AccountsFile, [
                "# RemSox accounts: id:name:group ids, one per account. Change them with the user and",
                "# group commands.",
                $"{HighestIdPrefix}{Math.Max(highestId, accounts.Select(a => a.Id).DefaultIfEmpty(Account.FirstUserId - 1).Max()).ToString(CultureInfo.InvariantCulture)}",
                .. accounts.Select(a => $"{a.Id.ToString(CultureInfo.InvariantCulture)}:{a.Name}:{string.Join(',', a.GroupIds.Select(id => id.ToString(CultureInfo.InvariantCulture)))}"),
            ]);

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SystemLog.Log("Could not save the accounts: " + ex.Message, LogSeverity.Error);
            return "Could not save the accounts: " + ex.Message;
        }
    }

    private static void ReadAccountLine(string line)
    {
        if (line.StartsWith(HighestIdPrefix, StringComparison.Ordinal))
        {
            if (int.TryParse(line[HighestIdPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int highest))
            {
                highestId = Math.Max(highestId, highest);
            }

            return;
        }

        if (line.Length == 0 || line[0] == '#')
        {
            return;
        }

        string[] parts = line.Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int id)
            || id < Account.FirstUserId
            || !IsWellFormedName(parts[1])
            || Find(parts[1]) is not null
            || Find(id) is not null)
        {
            SystemLog.Log($"{SystemPaths.AccountsFile}: ignored the line \"{line}\"", LogSeverity.Warning);
            return;
        }

        List<int> groupIds = [];
        foreach (string group in parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(group, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int groupId) && GroupStore.Find(groupId) is not null)
            {
                groupIds.Add(groupId);
            }
            else
            {
                SystemLog.Log($"{SystemPaths.AccountsFile}: {parts[1]} is in no group '{group}' that exists; ignored", LogSeverity.Warning);
            }
        }

        accounts.Add(new Account(id, parts[1].ToLowerInvariant(), groupIds));
        highestId = Math.Max(highestId, id);
    }

    private static void ReadPasswordLine(string line)
    {
        if (line.Length == 0 || line[0] == '#')
        {
            return;
        }

        int colon = line.IndexOf(':');
        if (colon <= 0
            || !int.TryParse(line[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out int id)
            || PasswordHash.Parse(line[(colon + 1)..]) is not { } hash)
        {
            SystemLog.Log($"{SystemPaths.PasswordsFile}: ignored a line that is not id:hash", LogSeverity.Warning);
            return;
        }

        passwords[id] = hash;
    }

    /// <summary>
    /// 1 to <see cref="MaxNameLength"/> ASCII letters, digits, <c>_</c> and <c>-</c>, starting with
    /// a letter: safe as a folder name on FAT and in the files' <c>:</c>-separated lines.
    /// </summary>
    internal static bool IsWellFormedName(string name)
    {
        return name.Length is > 0 and <= MaxNameLength
            && char.IsAsciiLetter(name[0])
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    }
}
