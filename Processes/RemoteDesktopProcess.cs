using RemSox.Networking;
using RemSox.Processing;
using RemSox.UI.GUI.Rendering;
using RemSox.UI.GUI.Windows;

namespace RemSox.Processes;

internal sealed class RemoteDesktopProcess() : Process("Remote Desktop Server")
{
    private TcpRpcServer? server;
    private NetworkRenderSource? networkSource;
    private CancellationTokenSource? cts;

    public int Port { get; private set; }

    internal override void Start(string[] args)
    {
        if (args.Length == 0 || !int.TryParse(args[0], out int port) || port <= 0 || port > 65535)
        {
            Logger.Log("Invalid port. Usage: rdp-start <port> (1-65535)", Logging.LogSeverity.Error);
            RequestStop();
            return;
        }

        Port = port;
        cts = new CancellationTokenSource();
        server = new TcpRpcServer();
        networkSource = new NetworkRenderSource(server);

        server.ListenTo<string>("SyncRequest", async _ =>
        {
            WindowManager.InvalidateAll();
        });

        _ = Task.Run(() => server.StartAsync(port, cts.Token));

        WindowManager.AddRenderSource(networkSource);

        Logger.Log($"Remote desktop server started on port {port}.", Logging.LogSeverity.Info);
    }

    internal override void Tick()
    {
    }

    internal override void Stop()
    {
        cts?.Cancel();
        server?.Stop();

        if (networkSource is not null)
        {
            WindowManager.RemoveRenderSource(networkSource);
        }

        Logger.Log("Remote desktop server stopped.", Logging.LogSeverity.Info);
    }
}
