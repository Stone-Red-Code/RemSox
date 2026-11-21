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
/// are stored as <see cref="PasswordHash"/>es; checking one takes a second or two in QEMU.
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
    /// system can be managed. Slow: the password is hashed here.
    /// </summary>
    /// <returns>The account, or why it could not be created.</returns>
    public static (Account? Account, string? Error) Create(string name, string password, bool admin)
    {
        MainLoop.AssertMainThread(nameof(Create));

        if (!IsAvailable)
        {
            return (null, FileSystemManager.NoDiskMessage);
        }

        name = name.ToLowerInvariant();
        if ((ValidateName(name) ?? ValidatePassword(password)) is { } error)
        {
            return (null, error);
        }

        Account account = new(highestId + 1, name, admin || !HasAccounts ? [Account.AdminsGroup] : []);
        PasswordHash hash = PasswordHash.Create(password, RpcHandshake.DefaultIterations);

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
        EnsureHomeFolder(account);
        SystemLog.Log($"Created account {account.Name} (id {account.Id}{(account.IsAdmin ? ", admin" : string.Empty)}).", LogSeverity.Info);
        return (account, null);
    }

    /// <summary>
    /// Deletes an account. Its home folder stays, and its id is never given again. The last
    /// admin cannot be deleted, so the system can always be managed.
    /// </summary>
    /// <returns>Null, or why it could not be deleted.</returns>
    public static string? Delete(Account account)
    {
        MainLoop.AssertMainThread(nameof(Delete));

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
        return null;
    }

    /// <summary>Sets an account's password. Slow: the password is hashed here.</summary>
    /// <returns>Null, or why it could not be set.</returns>
    public static string? SetPassword(Account account, string password)
    {
        MainLoop.AssertMainThread(nameof(SetPassword));

        if (ValidatePassword(password) is { } error)
        {
            return error;
        }

        PasswordHash? old = passwords.GetValueOrDefault(account.Id);
        passwords[account.Id] = PasswordHash.Create(password, RpcHandshake.DefaultIterations);

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
    /// Checks a name and password. Slow (a second or two in QEMU) whether or not the account
    /// exists, so the time taken does not tell which names exist. A password hashed with fewer
    /// rounds than today's default is hashed again, so old hashes get stronger over time.
    /// </summary>
    /// <returns>The account, or null when the name or the password is wrong.</returns>
    public static Account? Verify(string name, string password)
    {
        MainLoop.AssertMainThread(nameof(Verify));

        Account? account = Find(name);
        PasswordHash? hash = account is null ? null : passwords.GetValueOrDefault(account.Id);

        if (account is null || hash is null)
        {
            // Spend the same time as a real check.
            _ = Pkcs5S2PasswordKeyDeriver.DeriveKey(password, new byte[PasswordHash.SaltSize], RpcHandshake.DefaultIterations);
            return null;
        }

        if (!hash.Matches(password))
        {
            return null;
        }

        if (hash.Iterations < RpcHandshake.DefaultIterations)
        {
            _ = SetPassword(account, password);
        }

        return account;
    }

    /// <summary>Returns the account's password hash, or null when it has none.</summary>
    public static PasswordHash? GetPasswordHash(Account account)
    {
        return passwords.GetValueOrDefault(account.Id);
    }

    /// <summary>Creates the account's home folder if it is missing; failures are logged.</summary>
    public static void EnsureHomeFolder(Account account)
    {
        if (account.IsSystem || !IsAvailable)
        {
            return;
        }

        try
        {
            _ = Directory.CreateDirectory(account.HomeFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SystemLog.Log($"Could not create {account.HomeFolder}: {ex.Message}", LogSeverity.Error);
        }
    }

    private static string? Save()
    {
        try
        {
            SafeFile.WriteAllLines(SystemPaths.PasswordsFile, [
                "# RemSox passwords: id:hash, one per account. Secret; change them with passwd.",
                .. accounts.Where(a => passwords.ContainsKey(a.Id))
                    .Select(a => $"{a.Id.ToString(CultureInfo.InvariantCulture)}:{passwords[a.Id]}"),
            ]);

            SafeFile.WriteAllLines(SystemPaths.AccountsFile, [
                "# RemSox accounts: id:name:groups, one per account. Change them with useradd and userdel.",
                $"{HighestIdPrefix}{Math.Max(highestId, accounts.Select(a => a.Id).DefaultIfEmpty(0).Max()).ToString(CultureInfo.InvariantCulture)}",
                .. accounts.Select(a => $"{a.Id.ToString(CultureInfo.InvariantCulture)}:{a.Name}:{string.Join(',', a.Groups)}"),
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

        string[] groups = parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        accounts.Add(new Account(id, parts[1].ToLowerInvariant(), groups));
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
    private static bool IsWellFormedName(string name)
    {
        return name.Length is > 0 and <= MaxNameLength
            && char.IsAsciiLetter(name[0])
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    }
}
