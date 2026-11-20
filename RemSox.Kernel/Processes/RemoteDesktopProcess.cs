using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;

using RemSox.Shared.Networking;
using RemSox.Kernel.Processing;
using RemSox.Shared.UI;
using RemSox.Shared.UI.GUI.Rendering;
using RemSox.Kernel.UI.GUI.Rendering;
using RemSox.Kernel.UI.GUI.Windows;


namespace RemSox.Kernel.Processes;

internal sealed class RemoteDesktopProcess() : Process("Remote Desktop Server")
{
    private TcpRpcServer? server;
    private NetworkRenderSource? networkSource;
    private CancellationTokenSource? cts;

    /// <summary>Gets the port the server listens on.</summary>
    public int Port { get; private set; }

    /// <param name="args">The port, already checked by the <c>rdp</c> program's <c>Prepare</c>.</param>
    internal override void Start(string[] args)
    {
        int port = int.Parse(args[0]);
        Port = port;
        cts = new CancellationTokenSource();
        server = new TcpRpcServer
        {
            // Cosmos sockets are not safe to use from two threads at once.
            SerializeSocketIo = true,
            Log = message => Logger.Log(message, Logging.LogSeverity.Info),
        };
        networkSource = new NetworkRenderSource(server);

        server.ListenTo(RemoteInputProtocol.SyncRequest, _ =>
        {
            Canvas canvas = Canvas.GetFullScreen();
            RenderCommand screenInfo = new()
            {
                Type = RenderCommandType.ScreenInfo,
                WindowId = 0,
                ElementId = 0,
                Properties = new()
                {
                    ["Width"] = (int)canvas.Mode.Width,
                    ["Height"] = (int)canvas.Mode.Height,
                },
            };
            networkSource?.Render([screenInfo]);
            WindowManager.InvalidateAll();
            return Task.CompletedTask;
        });

        RegisterInputHandlers();

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
            networkSource.Dispose();
        }

        Logger.Log("Remote desktop server stopped.", Logging.LogSeverity.Info);
    }

    private void RegisterInputHandlers()
    {
        if (server is null)
        {
            return;
        }

        server.ListenTo(RemoteInputProtocol.MouseMove, payload =>
        {
            (int x, int y) = RemoteInputProtocol.DeserializeMouseMove(payload);
            WindowManager.EnqueueMouseEvent(MouseEvent.Move(x, y));
            return Task.CompletedTask;
        });

        server.ListenTo(RemoteInputProtocol.MouseDown, payload =>
        {
            (int x, int y, string button) = RemoteInputProtocol.DeserializeMouseButton(payload);
            WindowManager.EnqueueMouseEvent(MouseEvent.ButtonDown(x, y, ParseButton(button)));
            return Task.CompletedTask;
        });

        server.ListenTo(RemoteInputProtocol.MouseUp, payload =>
        {
            (int x, int y, string button) = RemoteInputProtocol.DeserializeMouseButton(payload);
            WindowManager.EnqueueMouseEvent(MouseEvent.ButtonUp(x, y, ParseButton(button)));
            return Task.CompletedTask;
        });

        server.ListenTo(RemoteInputProtocol.MouseWheel, payload =>
        {
            (int x, int y, int delta) = RemoteInputProtocol.DeserializeMouseWheel(payload);
            WindowManager.EnqueueMouseEvent(MouseEvent.Wheel(x, y, delta));
            return Task.CompletedTask;
        });

        server.ListenTo(RemoteInputProtocol.KeyEvent, payload =>
        {
            (int key, string keyChar, bool shift, bool alt, bool control, bool pressed) = RemoteInputProtocol.DeserializeKeyEvent(payload);

            // The local keyboard only queues presses, and windows don't check the event type,
            // so a release would be handled as a second press.
            if (!pressed)
            {
                return Task.CompletedTask;
            }

            KeyEvent keyEvent = new(keyChar.Length > 0 ? keyChar[0] : '\0', (Key)key, shift, alt, control, KeyEvent.KeyEventType.Make);
            WindowManager.EnqueueKeyEvent(keyEvent);
            return Task.CompletedTask;
        });
    }

    private static MouseButton ParseButton(string button)
    {
        return button switch
        {
            "Left" => MouseButton.Left,
            "Right" => MouseButton.Right,
            "Middle" => MouseButton.Middle,
            _ => MouseButton.None
        };
    }
}
