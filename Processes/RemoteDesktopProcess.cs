using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;

using RemSox.Networking;
using RemSox.Processing;
using RemSox.UI;
using RemSox.UI.GUI.Rendering;
using RemSox.UI.GUI.Windows;

namespace RemSox.Processes;

internal sealed class RemoteDesktopProcess() : Process("Remote Desktop Server")
{
    private TcpRpcServer? server;
    private NetworkRenderSource? networkSource;
    private CancellationTokenSource? cts;

    public int Port { get; private set; }

    // Remote input message record types (JSON-serialized over TCP)
    private sealed record MouseMoveMsg(int X, int Y);
    private sealed record MouseButtonMsg(int X, int Y, string Button);
    private sealed record MouseWheelMsg(int X, int Y, int Delta);
    private sealed record KeyEventMsg(int Key, string KeyChar, bool Shift, bool Alt, bool Control, bool Pressed);

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
            Canvas canvas = FullScreenCanvas.GetFullScreenCanvas();
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
        }

        Logger.Log("Remote desktop server stopped.", Logging.LogSeverity.Info);
    }

    private void RegisterInputHandlers()
    {
        if (server is null)
        {
            return;
        }

        server.ListenTo<MouseMoveMsg>("MouseMove", async (msg) =>
        {
            WindowManager.EnqueueMouseEvent(MouseEvent.Move(msg.X, msg.Y));
        });

        server.ListenTo<MouseButtonMsg>("MouseDown", async (msg) =>
        {
            MouseButton button = ParseButton(msg.Button);
            WindowManager.EnqueueMouseEvent(MouseEvent.ButtonDown(msg.X, msg.Y, button));
        });

        server.ListenTo<MouseButtonMsg>("MouseUp", async (msg) =>
        {
            MouseButton button = ParseButton(msg.Button);
            WindowManager.EnqueueMouseEvent(MouseEvent.ButtonUp(msg.X, msg.Y, button));
        });

        server.ListenTo<MouseWheelMsg>("MouseWheel", async (msg) =>
        {
            WindowManager.EnqueueMouseEvent(MouseEvent.Wheel(msg.X, msg.Y, msg.Delta));
        });

        server.ListenTo<KeyEventMsg>("KeyEvent", async (msg) =>
        {
            ConsoleKeyEx key = (ConsoleKeyEx)msg.Key;
            char keyChar = msg.KeyChar.Length > 0 ? msg.KeyChar[0] : '\0';
            bool isPressed = msg.Pressed;

            KeyEvent keyEvent = new(keyChar, key, msg.Shift, msg.Alt, msg.Control, isPressed ? KeyEvent.KeyEventType.Make : KeyEvent.KeyEventType.Break);
            WindowManager.EnqueueKeyEvent(keyEvent);
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
