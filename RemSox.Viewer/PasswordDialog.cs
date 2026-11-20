namespace RemSox.Viewer;

/// <summary>Asks for the remote desktop password (the server's <c>rdp.password</c> setting).</summary>
internal sealed class PasswordDialog : Form
{
    private readonly TextBox passwordBox = new() { UseSystemPasswordChar = true, Width = 260 };

    /// <param name="server">Shown so the user knows which server asks.</param>
    /// <param name="problem">Why it asks again (a wrong password), or null the first time.</param>
    public PasswordDialog(string server, string? problem)
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

        Label prompt = new()
        {
            Text = $"Password for {server}:",
            AutoSize = true,
        };

        Label error = new()
        {
            Text = problem ?? string.Empty,
            AutoSize = true,
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
        layout.Controls.AddRange([error, prompt, passwordBox, buttons]);
        Controls.Add(layout);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    /// <summary>Gets the password typed.</summary>
    public string Password => passwordBox.Text;

    /// <summary>
    /// Asks for the password over <paramref name="owner"/>; null when the user cancels or gives an
    /// empty one (the server never accepts that).
    /// </summary>
    public static string? Ask(IWin32Window? owner, string server, string? problem)
    {
        using PasswordDialog dialog = new(server, problem);
        return dialog.ShowDialog(owner) == DialogResult.OK && dialog.Password.Length > 0 ? dialog.Password : null;
    }
}
