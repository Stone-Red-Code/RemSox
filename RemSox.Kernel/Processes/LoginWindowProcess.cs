using Stopwatch = System.Diagnostics.Stopwatch;
using System.Drawing;

using RemSox.Kernel.Accounts;
using RemSox.Kernel.Processing;
using RemSox.Kernel.Programs;
using RemSox.Kernel.UI.GUI;
using RemSox.Kernel.UI.GUI.UIEelements.Controls;
using RemSox.Kernel.UI.GUI.UIEelements.Shapes;
using RemSox.Kernel.UI.GUI.Windows;

using Rectangle = RemSox.Kernel.UI.GUI.UIEelements.Shapes.Rectangle;

namespace RemSox.Kernel.Processes;

/// <summary>
/// The graphical login (<c>login.screen = graphical</c>): a window asking for a name and
/// password that, once they are right, starts the desktop in a new console session for that
/// account and closes. On a disk without accounts it creates the first one, an administrator.
/// </summary>
/// <remarks>
/// It runs as the system, like the console's login prompt: it is the one thing with a screen
/// that runs before anyone has logged in, and it can do nothing but log someone in. It is not
/// part of any desktop (no taskbar, no start menu), and it cannot be closed or moved.
/// </remarks>
internal sealed class LoginWindowProcess() : Process("Login")
{
    private const int Width = 380;
    private const int Padding = 16;
    private const int FieldHeight = 24;
    private const int LabelHeight = 18;
    private const int FontSize = 16;

    private Window window = null!;
    private TextBox nameBox = null!;
    private TextBox passwordBox = null!;
    private TextBox? repeatBox;
    private Text status = null!;

    // Whether the window creates the first account rather than logging in.
    private bool creating;
    private int failures;
    private long retryAt;

    internal override void Start(string[] args)
    {
        LocalDisplay.Start();
        creating = !AccountStore.HasAccounts;
        CreateWindow();
    }

    internal override void Tick()
    {
        LocalDisplay.PollInput();
        WindowManager.Update();
    }

    private void CreateWindow()
    {
        int fields = creating ? 3 : 2;
        int height = Padding + 26 + (creating ? LabelHeight + 6 : 0) + (fields * (LabelHeight + FieldHeight + 8)) + LabelHeight + 8 + FieldHeight + Padding;
        System.Drawing.Rectangle screen = WindowManager.GetScreenBounds();

        window = WindowManager.CreateWindow(this, "Log in", new Size(Width, height),
            new Point((screen.Width - Width) / 2, (screen.Height - height) / 2));
        window.HasChrome = false;
        window.IsResizable = false;
        window.IsDraggable = false;
        window.AutoFlush = true;
        window.OnCloseRequested += _ => { };

        _ = window.CreateUIElement<Rectangle>(r =>
        {
            r.Position = Point.Empty;
            r.Size = new Size(Width, height);
            r.IsFilled = true;
            r.Color = Color.FromArgb(40, 40, 46);
        });

        int y = Padding;
        AddText(creating ? "Welcome to RemSox" : "Log in to RemSox", 20, Color.White, ref y);
        y += 6;

        if (creating)
        {
            AddText("Create the first account, an administrator.", 14, Color.Gainsboro, ref y);
            y += 6;
        }

        nameBox = AddField("Name", ref y);
        passwordBox = AddField("Password", ref y);
        passwordBox.PasswordChar = '*';

        if (creating)
        {
            repeatBox = AddField("Repeat password", ref y);
            repeatBox.PasswordChar = '*';
        }

        status = AddText(string.Empty, 14, Color.FromArgb(240, 110, 100), ref y);
        y += 8;

        Button button = window.CreateUIElement<Button>(b =>
        {
            b.Position = new Point(Width - Padding - 140, y);
            b.Size = new Size(140, FieldHeight);
            b.Text = creating ? "Create account" : "Log in";
            b.BackgroundColor = Color.FromArgb(0, 100, 180);
            b.TextColor = Color.White;
        });
        button.OnClick += (_, _) => Submit();

        // Enter moves to the next field, and in the last one submits.
        nameBox.Submitted += _ => window.Focus(passwordBox);
        if (repeatBox is { } repeat)
        {
            passwordBox.Submitted += _ => window.Focus(repeat);
            repeat.Submitted += _ => Submit();
        }
        else
        {
            passwordBox.Submitted += _ => Submit();
        }

        window.Focus(nameBox);
    }

    private Text AddText(string content, int fontSize, Color color, ref int y)
    {
        int top = y;
        y += fontSize + 4;
        return window.CreateUIElement<Text>(t =>
        {
            t.Position = new Point(Padding, top);
            t.Content = content;
            t.FontSize = fontSize;
            t.Color = color;
        });
    }

    private TextBox AddField(string label, ref int y)
    {
        _ = AddText(label, 14, Color.Gainsboro, ref y);
        int top = y;
        y += FieldHeight + 8;
        return window.CreateUIElement<TextBox>(b =>
        {
            b.Position = new Point(Padding, top);
            b.Size = new Size(Width - (2 * Padding), FieldHeight);
            b.BackgroundColor = Color.FromArgb(20, 20, 24);
        });
    }

    private void Submit()
    {
        if (Stopwatch.GetTimestamp() < retryAt)
        {
            return;
        }

        string name = nameBox.Text.Trim();
        string password = passwordBox.Text;
        Account? account;

        if (creating)
        {
            if ((AccountStore.ValidateName(name.ToLowerInvariant()) ?? AccountStore.ValidatePassword(password)) is { } invalid)
            {
                Show(invalid);
                return;
            }

            if (repeatBox?.Text != password)
            {
                Show("The passwords are not the same.");
                return;
            }

            (account, string? error) = AccountStore.Create(name, password, admin: true);
            if (account is null)
            {
                Show(error!);
                return;
            }
        }
        else
        {
            account = AccountStore.Verify(name, password);
            if (account is null)
            {
                // Each failure makes the next try wait longer, so guessing is slow.
                failures++;
                retryAt = Stopwatch.GetTimestamp() + (Math.Min(2 * failures, 30) * Stopwatch.Frequency);
                passwordBox.Text = string.Empty;
                window.Focus(passwordBox);
                Show("Wrong name or password.");
                return;
            }
        }

        StartDesktop(account);
    }

    /// <summary>Starts the desktop in a new console session for <paramref name="account"/>, then closes.</summary>
    private void StartDesktop(Account account)
    {
        LogonSession session = LogonSessions.Start(account, LogonKind.Console, "screen");
        ProgramStart start = ProgramRegistry.Find(BuiltInPrograms.DesktopName) is { } desktop
            ? ProgramRegistry.Start(desktop, [], account.HomeFolder, session)
            : ProgramStart.Fail("There is no desktop program.");

        if (start.Error is not null)
        {
            LogonSessions.End(session);
            Show(start.Error);
            return;
        }

        ProcessManager.StopProcess(Id);
    }

    private void Show(string message)
    {
        status.Content = message;
    }
}
