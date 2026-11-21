using System.Diagnostics;

using RemSox.Kernel.Accounts;
using RemSox.Kernel.Logging;

namespace RemSox.Kernel.UI.CLI.Commands;

/// <summary>
/// Runs one command line with admin rights, for an administrator who gives their password, as on
/// Linux. Administrators otherwise work with ordinary rights, so a slip cannot damage the system
/// and someone at an administrator's unattended shell cannot use admin rights without the
/// password. A correct password is remembered for a few minutes in that shell.
/// </summary>
public sealed class SudoCommand : ICommand
{
    /// <summary>How long a shell runs sudo without asking again after the password was given.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    public string Name => "sudo";

    public string Description => "Run a command with administrator rights (administrators only; asks for your password)";

    public string Usage => "sudo <command> [arguments...]";

    public int MinArguments => 1;

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        ShellSession session = context.Session;
        Account account = session.Account;

        if (!account.IsAdmin)
        {
            return context.Fail($"{account.Name} is not an administrator, so sudo cannot give admin rights.");
        }

        if (!context.Elevated && Stopwatch.GetTimestamp() >= session.SudoValidUntil)
        {
            string? password = await context.ReadSecretAsync($"[sudo] password for {account.Name}: ");
            if (password is null)
            {
                return context.Fail(context.Cancellation.IsCancellationRequested ? "Cancelled." : "This shell cannot ask for a password.");
            }

            if (AccountStore.Verify(account.Name, password) is null)
            {
                SystemLog.Log($"sudo: {account.Name} gave a wrong password ({session.Name}).", LogSeverity.Warning);
                return context.Fail("Wrong password.");
            }

            session.SudoValidUntil = Stopwatch.GetTimestamp() + (long)(Grace.TotalSeconds * Stopwatch.Frequency);
        }

        SystemLog.Log($"sudo: {account.Name} ran '{context.RawArguments}' ({session.Name}).", LogSeverity.Info);
        return await CommandManager.ExecuteAsync(context.RawArguments, session, context.Console, context.Cancellation, addToHistory: false, elevated: true);
    }
}
