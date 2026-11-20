namespace RemSox.Viewer;

public static class Program
{
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 9999;

    /// <summary>The environment variable that can hold the password, so scripts need not type it.</summary>
    private const string PasswordVariable = "REMSOX_RDP_PASSWORD";

    /// <summary>
    /// Usage: RemSox.Viewer [host] [port]. Defaults to 127.0.0.1:9999, the port build-and-run.ps1
    /// forwards. The password (the server's rdp.password setting) is asked for, unless it is in
    /// the REMSOX_RDP_PASSWORD environment variable.
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
        string? password = Environment.GetEnvironmentVariable(PasswordVariable);
        Application.Run(new ViewerForm(host, port, string.IsNullOrEmpty(password) ? null : password));
    }
}
