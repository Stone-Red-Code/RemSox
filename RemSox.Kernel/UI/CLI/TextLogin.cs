using System.Diagnostics;

using RemSox.Kernel.Accounts;

namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// The login prompt of a text terminal (the console, a Telnet client), shown before any shell
/// exists: <c>login:</c>, then <c>password:</c> (not echoed). On a disk without accounts the
/// console instead asks for the first account, which becomes an administrator. Like the shell,
/// it is fed keys on the main loop and draws on an <see cref="IShellTerminal"/>.
/// </summary>
/// <remarks>
/// A failed login makes the next one wait (2 s, longer after each failure), and checking a
/// password takes a second or two, so guessing is slow. The next prompt appears only once the
/// wait is over, which the owner's loop finds out by calling <see cref="Tick"/>. Where
/// <c>maxFailures</c> is set, that many failures in a row end the login (<see cref="GaveUp"/>):
/// Telnet then disconnects. All of that holds for one prompt only; an owner whose clients can
/// come back or come in numbers (Telnet) passes an <see cref="ILoginGate"/>, which is asked
/// before each password is checked and told how it went.
/// </remarks>
public sealed class TextLogin(IShellTerminal terminal, LogonKind kind, string origin, bool canCreateFirstAccount, int maxFailures, ILoginGate? gate = null)
{
    private const int MaxLength = 256;
    private const char ControlC = '\x03';
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private Step step = Step.Done;
    private string text = string.Empty;
    private string name = string.Empty;
    private string newPassword = string.Empty;
    private int failures;

    // While waiting after a failed login: when the next prompt may appear. Read by Telnet's
    // session thread to know when to call Tick.
    private volatile bool waiting;
    private long retryAt;

    private enum Step
    {
        Name,
        Password,
        NewName,
        NewPassword,
        RepeatPassword,
        Done,
    }

    /// <summary>Raised on the main loop with the new session once someone logged in.</summary>
    public event Action<LogonSession>? LoggedIn;

    /// <summary>
    /// Raised when nobody can log in here: no disk, no accounts (where none can be created), or
    /// too many failures. What it said is already written.
    /// </summary>
    public event Action? GaveUp;

    /// <summary>Shows the first prompt, or says why nobody can log in.</summary>
    public void Start()
    {
        if (!AccountStore.IsAvailable)
        {
            terminal.WriteError("No disk is mounted, so there are no accounts to log in to.");
            End();
            return;
        }

        if (!AccountStore.HasAccounts)
        {
            if (!canCreateFirstAccount)
            {
                terminal.WriteError("There are no accounts yet. Create the first one at the machine's console.");
                End();
                return;
            }

            terminal.WriteLine("There are no accounts yet. Create the first one; it will be an administrator.");
            Ask(Step.NewName);
            return;
        }

        Ask(Step.Name);
    }

    /// <summary>Gets whether a failed login is being waited out; <see cref="Tick"/> ends the wait once it is due.</summary>
    public bool IsWaiting => waiting;

    /// <summary>Gets whether the wait after a failed login is over, so <see cref="Tick"/> would show the prompt. Safe from any thread.</summary>
    public bool IsWaitOver => waiting && Stopwatch.GetTimestamp() >= Interlocked.Read(ref retryAt);

    /// <summary>Shows the prompt again once the wait after a failed login is over; call it from the owner's loop.</summary>
    public void Tick()
    {
        if (IsWaitOver)
        {
            waiting = false;
            Ask(Step.Name);
        }
    }

    /// <summary>Handles one key typed at the prompt.</summary>
    public void HandleKey(ConsoleKeyInfo key)
    {
        // Keys typed while a failed login is still being paid for are dropped: no prompt is shown then.
        if (step == Step.Done || waiting)
        {
            return;
        }

        if (key.KeyChar == ControlC || (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)))
        {
            terminal.ShowInput(Prompt + Shown + "^C");
            terminal.CommitInput();
            Ask(step is Step.Name or Step.Password ? Step.Name : Step.NewName);
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter:
                Submit();
                return;

            case ConsoleKey.Backspace:
                if (text.Length > 0)
                {
                    text = text[..^1];
                }
                break;

            default:
                if (!char.IsControl(key.KeyChar) && text.Length < MaxLength)
                {
                    text += key.KeyChar;
                }
                break;
        }

        Show();
    }

    private string Prompt => step switch
    {
        Step.Name => "login: ",
        Step.Password or Step.NewPassword => "password: ",
        Step.NewName => "new account name: ",
        Step.RepeatPassword => "repeat password: ",
        _ => string.Empty,
    };

    private string Shown => step is Step.Password or Step.NewPassword or Step.RepeatPassword ? string.Empty : text;

    private void Submit()
    {
        string value = text;
        text = string.Empty;
        terminal.CommitInput();

        switch (step)
        {
            case Step.Name:
                name = value.Trim();
                Ask(name.Length == 0 ? Step.Name : Step.Password);
                break;

            case Step.Password:
                if (gate?.Refuse(name) is { } refusal)
                {
                    Fail(refusal);
                    break;
                }

                Account? account = AccountStore.Verify(name, value);
                gate?.Report(name, rightPassword: account is not null);
                if (account is not null)
                {
                    Finish(account);
                }
                else
                {
                    Fail("Login incorrect.");
                }
                break;

            case Step.NewName:
                name = value.Trim().ToLowerInvariant();
                if (AccountStore.ValidateName(name) is { } nameError)
                {
                    terminal.WriteError(nameError);
                    Ask(Step.NewName);
                }
                else
                {
                    Ask(Step.NewPassword);
                }
                break;

            case Step.NewPassword:
                if (AccountStore.ValidatePassword(value) is { } passwordError)
                {
                    terminal.WriteError(passwordError);
                    Ask(Step.NewPassword);
                }
                else
                {
                    newPassword = value;
                    Ask(Step.RepeatPassword);
                }
                break;

            case Step.RepeatPassword:
                if (value != newPassword)
                {
                    terminal.WriteError("The passwords are not the same.");
                    Ask(Step.NewPassword);
                    break;
                }

                (Account? created, string? error) = AccountStore.Create(name, newPassword, admin: true);
                newPassword = string.Empty;
                if (created is null)
                {
                    terminal.WriteError(error!);
                    Ask(Step.NewName);
                    break;
                }

                terminal.WriteLine($"Created {created.Name}, an administrator.");
                Finish(created);
                break;
        }
    }

    private void Fail(string message)
    {
        failures++;
        terminal.WriteError(message);

        if (maxFailures > 0 && failures >= maxFailures)
        {
            End();
            return;
        }

        TimeSpan delay = TimeSpan.FromSeconds(Math.Min(2 * failures, MaxRetryDelay.TotalSeconds));
        Interlocked.Exchange(ref retryAt, Stopwatch.GetTimestamp() + (long)(delay.TotalSeconds * Stopwatch.Frequency));
        step = Step.Name;
        text = string.Empty;
        terminal.ShowInput(null);
        waiting = true;
    }

    private void Finish(Account account)
    {
        step = Step.Done;
        terminal.ShowInput(null);
        LoggedIn?.Invoke(LogonSessions.Start(account, kind, origin));
    }

    private void End()
    {
        step = Step.Done;
        terminal.ShowInput(null);
        GaveUp?.Invoke();
    }

    private void Ask(Step next)
    {
        step = next;
        text = string.Empty;
        Show();
    }

    private void Show()
    {
        terminal.ShowInput(Prompt + Shown);
    }
}
