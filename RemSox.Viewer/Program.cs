namespace RemSox.Viewer;

public static class Program
{
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 9999;

    /// <summary>The environment variables that can hold the account to log in as, so scripts need not type it.</summary>
    private const string UserVariable = "REMSOX_RDP_USER";

    /// <inheritdoc cref="UserVariable"/>
    private const string PasswordVariable = "REMSOX_RDP_PASSWORD";

    /// <summary>
    /// Usage: RemSox.Viewer [host] [port]. Defaults to 127.0.0.1:9999, the port build-and-run.ps1
    /// forwards. The RemSox account to log in as is asked for, unless the REMSOX_RDP_USER and
    /// REMSOX_RDP_PASSWORD environment variables hold it.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        string host = args.Length > 0 ? args[0] : DefaultHost;
        int port = DefaultPort;

        if (args.Length > 1 && (!int.TryParse(args[1], out port) || port is <= 0 or > 65535))
        {
            _ = MessageBox.Show($"Invalid port: {args[1]}", "RemSox Viewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        ApplicationConfiguration.Initialize();
        string? user = Environment.GetEnvironmentVariable(UserVariable);
        string? password = Environment.GetEnvironmentVariable(PasswordVariable);
        Credentials? credentials = string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password) ? null : new Credentials(user, password);
        Application.Run(new ViewerForm(host, port, credentials));
    }
}
