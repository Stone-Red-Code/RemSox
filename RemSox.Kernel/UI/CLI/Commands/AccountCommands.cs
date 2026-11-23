using System.Globalization;

using RemSox.Kernel.Accounts;
using RemSox.Kernel.Processes;

namespace RemSox.Kernel.UI.CLI.Commands;

/// <summary>Shared checks of the account and group commands.</summary>
internal static class AccountCommandHelpers
{
    /// <summary>
    /// Returns the failure to report unless the command runs with admin rights (an administrator
    /// with <c>admin</c>), else null.
    /// </summary>
    /// <param name="what">What the caller wants to do, completing "Only an administrator can ...".</param>
    public static int? RequireAdmin(CommandContext context, string what)
    {
        return context.Caller.DenyUnlessAdmin(what) is { } denied ? context.Fail(denied) : null;
    }

    /// <summary>
    /// Asks for a new password twice, without showing it. Passwords are never taken as
    /// arguments, so they cannot end up in a shell's history or a script.
    /// </summary>
    /// <returns>The password, or null after reporting why there is none.</returns>
    public static async Task<string?> AskNewPasswordAsync(CommandContext context, string prompt)
    {
        string? password = await context.ReadSecretAsync(prompt);
        if (password is null)
        {
            context.Console.WriteError(context.Cancellation.IsCancellationRequested ? "Cancelled." : "This shell cannot ask for a password.");
            return null;
        }

        if (AccountStore.ValidatePassword(password) is { } error)
        {
            context.Console.WriteError(error);
            return null;
        }

        if (await context.ReadSecretAsync("Repeat it: ") != password)
        {
            context.Console.WriteError("The passwords are not the same.");
            return null;
        }

        return password;
    }

    public static string FormatGroups(Account account)
    {
        return account.GroupIds.Count == 0 ? "-" : string.Join(',', account.GroupIds.Select(id => GroupStore.Find(id)?.Name ?? id.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Finds an account by name, or reports that there is none.</summary>
    public static Account? FindAccount(CommandContext context, string name)
    {
        Account? account = AccountStore.Find(name);
        if (account is null)
        {
            context.Console.WriteError($"There is no account called {name}.");
        }

        return account;
    }
}

/// <summary>
/// Everything about accounts, as one command with an action, the way <c>config</c> and
/// <c>service</c> work: show one, list them, add, remove and change a password. Without an action
/// it shows how to use it rather than guessing one.
/// </summary>
public sealed class UserCommand : ICommand
{
    private const string AdminOption = "--admin";

    public string Name => "user";

    public string Description => "Show an account, list them, add or remove one, change a password";

    public string Usage => $"user show [name] | user list | user add <name> [{AdminOption}] | user remove <name> | user password [name]";

    public Task<int> ExecuteAsync(CommandContext context)
    {
        IReadOnlyList<string> args = context.Args;
        string action = args.Count > 0 ? args[0].ToLowerInvariant() : string.Empty;

        return (action, args.Count) switch
        {
            ("show", <= 2) => Task.FromResult(Show(context, args.Count == 2 ? args[1] : null)),
            ("list", 1) => Task.FromResult(List(context)),
            ("add", 2 or 3) => AddAsync(context),
            ("remove", 2) => RemoveAsync(context, args[1]),
            ("password", <= 2) => ChangePasswordAsync(context, args.Count == 2 ? args[1] : null),
            _ => Task.FromResult(context.UsageError()),
        };
    }

    /// <summary>Shows an account (your own without a name): its id and groups. Sessions are <c>session list</c>'s.</summary>
    private static int Show(CommandContext context, string? name)
    {
        Account? account = name is null ? context.Session.Account : AccountCommandHelpers.FindAccount(context, name);
        if (account is null)
        {
            return CommandResult.Failure;
        }

        context.Console.WriteLine($"{account.Name} (id {account.Id.ToString(CultureInfo.InvariantCulture)}, groups: {AccountCommandHelpers.FormatGroups(account)})");
        return CommandResult.Success;
    }

    private static int List(CommandContext context)
    {
        if (!AccountStore.IsAvailable)
        {
            return context.Fail("No disk is mounted, so there are no accounts.");
        }

        foreach (Account account in AccountStore.All)
        {
            context.Console.WriteLine($"{account.Id,5}  {account.Name,-16} {AccountCommandHelpers.FormatGroups(account)}");
        }

        return CommandResult.Success;
    }

    /// <summary>Creates an account, asking for its password.</summary>
    private static async Task<int> AddAsync(CommandContext context)
    {
        if (AccountCommandHelpers.RequireAdmin(context, "add accounts") is { } denied)
        {
            return denied;
        }

        bool admin = context.Args.Count == 3 && context.Args[2] == AdminOption;
        if (context.Args.Count == 3 && !admin)
        {
            return context.UsageError();
        }

        string name = context.Args[1].ToLowerInvariant();
        if (AccountStore.ValidateName(name) is { } nameError)
        {
            return context.Fail(nameError);
        }

        if (await AccountCommandHelpers.AskNewPasswordAsync(context, $"Password for {name}: ") is not { } password)
        {
            return CommandResult.Failure;
        }

        (Account? account, string? error) = AccountStore.Create(name, password, admin);
        if (account is null)
        {
            return context.Fail(error!);
        }

        context.Console.WriteLine($"Created {account.Name} (id {account.Id.ToString(CultureInfo.InvariantCulture)}){(account.IsAdmin ? ", an administrator" : string.Empty)}.");
        return CommandResult.Success;
    }

    /// <summary>Deletes an account and logs it out; its profile folder is kept as <c>&lt;name&gt;.deleted</c>.</summary>
    private static async Task<int> RemoveAsync(CommandContext context, string name)
    {
        if (AccountCommandHelpers.RequireAdmin(context, "remove accounts") is { } denied)
        {
            return denied;
        }

        if (AccountCommandHelpers.FindAccount(context, name) is not { } account)
        {
            return CommandResult.Failure;
        }

        if (account.Id == context.Session.Account.Id)
        {
            return context.Fail("You cannot remove the account you are logged in as.");
        }

        if (!await context.ConfirmAsync($"Remove {account.Name} and log out its sessions?"))
        {
            return CommandResult.Cancelled;
        }

        if (AccountStore.Delete(account, out string? keptProfile) is { } error)
        {
            return context.Fail(error);
        }

        foreach (LogonSession session in LogonSessions.Active.Where(s => s.Account.Id == account.Id).ToList())
        {
            LogonSessions.End(session);
        }

        context.Console.WriteLine(keptProfile is not null
            ? $"Removed {account.Name}. Its files are kept in {keptProfile}."
            : $"Removed {account.Name}.");
        return CommandResult.Success;
    }

    /// <summary>Changes your own password (asking for the current one), or as an administrator anyone's.</summary>
    private static async Task<int> ChangePasswordAsync(CommandContext context, string? name)
    {
        Account self = context.Session.Account;
        if (self.IsSystem && name is null)
        {
            return context.Fail("The system account has no password. Name the account to change.");
        }

        Account? account = name is null ? self : AccountCommandHelpers.FindAccount(context, name);
        if (account is null)
        {
            return CommandResult.Failure;
        }

        if (account.Id != self.Id)
        {
            if (AccountCommandHelpers.RequireAdmin(context, "change another account's password") is { } denied)
            {
                return denied;
            }
        }
        else
        {
            // Proves it is still the account's owner at the keyboard, not someone at a shell they left open.
            string? current = await context.ReadSecretAsync("Current password: ");
            if (current is null)
            {
                return context.Fail(context.Cancellation.IsCancellationRequested ? "Cancelled." : "This shell cannot ask for a password.");
            }

            if (AccountStore.Verify(account.Name, current) is null)
            {
                return context.Fail("That is not the current password.");
            }
        }

        if (await AccountCommandHelpers.AskNewPasswordAsync(context, $"New password for {account.Name}: ") is not { } password)
        {
            return CommandResult.Failure;
        }

        if (AccountStore.SetPassword(account, password) is { } error)
        {
            return context.Fail(error);
        }

        context.Console.WriteLine($"Changed the password of {account.Name}.");
        return CommandResult.Success;
    }
}

/// <summary>Shows the logon sessions: who is logged in, where from and since when.</summary>
public sealed class SessionCommand : ICommand
{
    public string Name => "session";

    public string Description => "List who is logged in, where each session is now, how it logged in and when (* marks yours)";

    public string Usage => "session list";

    public int MinArguments => 1;

    public int MaxArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        if (!context.Args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(context.UsageError());
        }

        if (LogonSessions.Active.Count == 0)
        {
            context.Console.WriteLine("Nobody is logged in.");
            return Task.FromResult(CommandResult.Success);
        }

        context.Console.WriteLine($"   ID  {"USER",-12} {"NOW",-15} {"LOGGED IN",-30} SINCE");
        foreach (LogonSession session in LogonSessions.Active)
        {
            string current = session == context.Session.Logon ? "*" : " ";
            string loggedIn = $"{Describe(session.Kind)}, {session.Origin}";
            context.Console.WriteLine($" {current}{session.Id,3}  {session.Account.Name,-12} {Describe(session.Place),-15} {loggedIn,-30} {session.StartedAt:HH:mm:ss}");
        }

        return Task.FromResult(CommandResult.Success);
    }

    private static string Describe(SessionPlace place)
    {
        return place switch
        {
            SessionPlace.Machine => "at the machine",
            SessionPlace.RemoteDesktop => "remote desktop",
            SessionPlace.Telnet => "telnet",
            SessionPlace.Locked => "locked",
            _ => "system",
        };
    }

    private static string Describe(LogonKind kind)
    {
        return kind switch
        {
            LogonKind.Local => "local",
            LogonKind.Telnet => "telnet",
            LogonKind.RemoteDesktop => "remote desktop",
            _ => "system",
        };
    }
}

/// <summary>
/// Logs out: this session, or one named by its number (from <c>session list</c>). Any account
/// may end its own sessions, such as a desktop left running elsewhere; ending someone else's
/// needs admin rights, like Windows' sign off from Task Manager.
/// </summary>
public sealed class LogoutCommand : ICommand
{
    public string Name => "logout";

    public string Description => "Log out: end this session, or the one numbered, and everything started in it";

    public string Usage => "logout [session]";

    public int MaxArguments => 1;

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        if (context.Args.Count == 0)
        {
            if (context.Session.Logon.IsSystem)
            {
                return context.Fail("This shell runs as the system, which is never logged in. Name the session to end.");
            }

            LogonSessions.End(context.Session.Logon);
            return CommandResult.Success;
        }

        if (!int.TryParse(context.Args[0], NumberStyles.None, CultureInfo.InvariantCulture, out int id))
        {
            return context.UsageError();
        }

        if (LogonSessions.Active.FirstOrDefault(s => s.Id == id) is not { } session)
        {
            return context.Fail($"There is no session {id}; 'session list' shows them.");
        }

        if (session.Account.Id != context.Session.Account.Id)
        {
            if (AccountCommandHelpers.RequireAdmin(context, "end someone else's session") is { } denied)
            {
                return denied;
            }

            // Whatever its user has open and unsaved is lost.
            if (!await context.ConfirmAsync($"Log out {session.Account.Name}'s session {id} and stop everything in it?"))
            {
                return CommandResult.Cancelled;
            }
        }

        // Said first: ending this shell's own session stops the shell.
        if (session != context.Session.Logon)
        {
            context.Console.WriteLine($"Logged out {session.Account.Name}'s session {id}.");
        }

        LogonSessions.End(session);
        return CommandResult.Success;
    }
}

public sealed class LockCommand : ICommand
{
    public string Name => "lock";

    public string Description => "Lock this desktop session: the login screen shows until you log in again";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        LogonSession session = context.Session.Logon;

        // Locking anything else would leave its shell usable: only a desktop is hidden by it.
        if (DesktopProcess.FindSession(session.Account) != session)
        {
            return Task.FromResult(context.Fail("Only a desktop session can be locked; this is a " + session.Kind + " session without one."));
        }

        LogonSessions.Lock(session);
        return Task.FromResult(CommandResult.Success);
    }
}
