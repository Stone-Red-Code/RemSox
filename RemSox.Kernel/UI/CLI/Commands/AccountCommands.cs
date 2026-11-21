using System.Globalization;

using RemSox.Kernel.Accounts;

namespace RemSox.Kernel.UI.CLI.Commands;

/// <summary>Shared checks of the account commands.</summary>
internal static class AccountCommandHelpers
{
    /// <summary>Returns the failure to report when the shell's account is no administrator, else null.</summary>
    public static int? RequireAdmin(CommandContext context)
    {
        return context.Session.Account.IsAdmin ? null : context.Fail($"Only an administrator can use {context.Command.Name}.");
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
        return account.Groups.Count == 0 ? "-" : string.Join(',', account.Groups);
    }
}

public sealed class WhoAmICommand : ICommand
{
    public string Name => "whoami";

    public string Description => "Show the account this shell runs as";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        Account account = context.Session.Account;
        LogonSession logon = context.Session.Logon;

        context.Console.WriteLine($"{account.Name} (id {account.Id.ToString(CultureInfo.InvariantCulture)}, groups: {AccountCommandHelpers.FormatGroups(account)})");
        if (!logon.IsSystem)
        {
            context.Console.WriteLine($"Session {logon.Id.ToString(CultureInfo.InvariantCulture)}: {logon.Kind} from {logon.Origin}, since {logon.StartedAt:HH:mm:ss}");
        }

        return Task.FromResult(CommandResult.Success);
    }
}

public sealed class WhoCommand : ICommand
{
    public string Name => "who";

    public string Description => "List who is logged in";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        if (LogonSessions.Active.Count == 0)
        {
            context.Console.WriteLine("Nobody is logged in.");
        }

        foreach (LogonSession session in LogonSessions.Active)
        {
            string current = session == context.Session.Logon ? "  (this shell)" : string.Empty;
            context.Console.WriteLine($"{session.Id,3}  {session.Account.Name,-16} {session.Kind,-13} {session.Origin,-22} since {session.StartedAt:HH:mm:ss}{current}");
        }

        return Task.FromResult(CommandResult.Success);
    }
}

public sealed class UsersCommand : ICommand
{
    public string Name => "users";

    public string Description => "List the accounts";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        if (!AccountStore.IsAvailable)
        {
            return Task.FromResult(context.Fail("No disk is mounted, so there are no accounts."));
        }

        foreach (Account account in AccountStore.All)
        {
            context.Console.WriteLine($"{account.Id,5}  {account.Name,-16} {AccountCommandHelpers.FormatGroups(account)}");
        }

        return Task.FromResult(CommandResult.Success);
    }
}

public sealed class AddUserCommand : ICommand
{
    private const string AdminOption = "--admin";

    public string Name => "useradd";

    public string Description => "Create an account (asks for its password; administrators only)";

    public string Usage => $"useradd <name> [{AdminOption}]";

    public int MinArguments => 1;

    public int MaxArguments => 2;

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        if (AccountCommandHelpers.RequireAdmin(context) is { } denied)
        {
            return denied;
        }

        bool admin = context.Args.Count == 2 && context.Args[1] == AdminOption;
        if (context.Args.Count == 2 && !admin)
        {
            return context.UsageError();
        }

        string name = context.Args[0].ToLowerInvariant();
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
}

public sealed class DeleteUserCommand : ICommand
{
    public string Name => "userdel";

    public string Description => "Delete an account and log it out; its profile folder stays (administrators only)";

    public string Usage => "userdel <name>";

    public int MinArguments => 1;

    public int MaxArguments => 1;

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        if (AccountCommandHelpers.RequireAdmin(context) is { } denied)
        {
            return denied;
        }

        if (AccountStore.Find(context.Args[0]) is not { } account)
        {
            return context.Fail($"There is no account called {context.Args[0]}.");
        }

        if (account.Id == context.Session.Account.Id)
        {
            return context.Fail("You cannot delete the account you are logged in as.");
        }

        if (!await context.ConfirmAsync($"Delete {account.Name} and log out its sessions?"))
        {
            return CommandResult.Cancelled;
        }

        if (AccountStore.Delete(account) is { } error)
        {
            return context.Fail(error);
        }

        foreach (LogonSession session in LogonSessions.Active.Where(s => s.Account.Id == account.Id).ToList())
        {
            LogonSessions.End(session);
        }

        context.Console.WriteLine($"Deleted {account.Name}. Its files are still in {account.ProfileFolder}.");
        return CommandResult.Success;
    }
}

public sealed class PasswordCommand : ICommand
{
    public string Name => "passwd";

    public string Description => "Change your password, or (administrators) anyone's";

    public string Usage => "passwd [name]";

    public int MaxArguments => 1;

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        Account self = context.Session.Account;
        if (self.IsSystem && context.Args.Count == 0)
        {
            return context.Fail("The system account has no password. Name the account to change.");
        }

        Account? account = context.Args.Count == 0 ? self : AccountStore.Find(context.Args[0]);
        if (account is null)
        {
            return context.Fail($"There is no account called {context.Args[0]}.");
        }

        if (account.Id != self.Id)
        {
            if (AccountCommandHelpers.RequireAdmin(context) is { } denied)
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

public sealed class LogoutCommand : ICommand
{
    public string Name => "logout";

    public string Description => "Log out: end this session and everything started in it";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        if (context.Session.Logon.IsSystem)
        {
            return Task.FromResult(context.Fail("This shell runs as the system, which is never logged in."));
        }

        LogonSessions.End(context.Session.Logon);
        return Task.FromResult(CommandResult.Success);
    }
}
