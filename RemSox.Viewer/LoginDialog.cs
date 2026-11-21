namespace RemSox.Viewer;

/// <summary>The name and password of the RemSox account a viewer logs in as.</summary>
internal sealed record Credentials(string User, string Password);

/// <summary>Asks for the account to log in to the remote desktop as.</summary>
internal sealed class LoginDialog : Form
{
    private readonly TextBox userBox = new() { Width = 260 };
    private readonly TextBox passwordBox = new() { UseSystemPasswordChar = true, Width = 260 };

    /// <param name="server">Shown so the user knows which server asks.</param>
    /// <param name="user">The name to fill in (the last one tried), or null.</param>
    /// <param name="problem">Why it asks again (a wrong password, a refusal, the session ended), or null the first time.</param>
    public LoginDialog(string server, string? user, string? problem)
    {
        Text = "RemSox Viewer";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        userBox.Text = user ?? string.Empty;

        Label error = new()
        {
            Text = problem ?? string.Empty,
            AutoSize = true,
            MaximumSize = new Size(260, 0),
            ForeColor = Color.Firebrick,
            Visible = problem is not null,
        };

        Button ok = new() { Text = "Connect", DialogResult = DialogResult.OK, AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };

        FlowLayoutPanel buttons = new()
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        buttons.Controls.AddRange([cancel, ok]);

        TableLayoutPanel layout = new()
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        layout.Controls.AddRange([
            error,
            new Label { Text = $"Log in to {server} as:", AutoSize = true },
            userBox,
            new Label { Text = "Password:", AutoSize = true },
            passwordBox,
            buttons,
        ]);
        Controls.Add(layout);

        AcceptButton = ok;
        CancelButton = cancel;

        // With the name known, the password is what is left to type.
        Shown += (_, _) => (user is null ? userBox : passwordBox).Focus();
    }

    /// <summary>
    /// Asks for the account over <paramref name="owner"/>; null when the user cancels or leaves
    /// the name or password empty (the server never accepts that).
    /// </summary>
    public static Credentials? Ask(IWin32Window? owner, string server, string? user, string? problem)
    {
        using LoginDialog dialog = new(server, user, problem);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            return null;
        }

        string name = dialog.userBox.Text.Trim();
        return name.Length > 0 && dialog.passwordBox.Text.Length > 0 ? new Credentials(name, dialog.passwordBox.Text) : null;
    }
}
