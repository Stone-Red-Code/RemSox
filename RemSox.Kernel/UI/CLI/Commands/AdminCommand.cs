using System.Diagnostics;

using RemSox.Kernel.Accounts;
using RemSox.Kernel.Logging;

namespace RemSox.Kernel.UI.CLI.Commands;

/// <summary>
/// Runs one command line with admin rights, for an administrator who gives their password.
/// Administrators otherwise work with ordinary rights, so a slip cannot damage the system and
/// someone at an administrator's unattended shell cannot use admin rights without the password.
/// A correct password is remembered for a few minutes in that shell.
/// </summary>
public sealed class AdminCommand : ICommand
{
    /// <summary>How long a shell runs <c>admin</c> without asking again after the password was given.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    public string Name => "admin";

    public string Description => "Run a command with administrator rights (administrators only; asks for your password)";

    public string Usage => "admin <command> [arguments...]";

    public int MinArguments => 1;

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        ShellSession session = context.Session;
        Account account = session.Account;

        if (!account.IsAdmin)
        {
            return context.Fail($"{account.Name} is not an administrator, so 'admin' cannot give admin rights.");
        }

        if (!context.Elevated && Stopwatch.GetTimestamp() >= session.AdminValidUntil)
        {
            string? password = await context.ReadSecretAsync($"Password for {account.Name} (to run as administrator): ");
            if (password is null)
            {
                return context.Fail(context.Cancellation.IsCancellationRequested ? "Cancelled." : "This shell cannot ask for a password.");
            }

            if (await AccountStore.VerifyAsync(account.Name, password) is null)
            {
                SystemLog.Log($"admin: {account.Name} gave a wrong password ({session.Name}).", LogSeverity.Warning);
                return context.Fail("Wrong password.");
            }

            session.AdminValidUntil = Stopwatch.GetTimestamp() + (long)(Grace.TotalSeconds * Stopwatch.Frequency);
        }

        SystemLog.Log($"admin: {account.Name} ran '{context.RawArguments}' ({session.Name}).", LogSeverity.Info);
        return await CommandManager.ExecuteAsync(context.RawArguments, session, context.Console, context.Cancellation, addToHistory: false, elevated: true);
    }
}
