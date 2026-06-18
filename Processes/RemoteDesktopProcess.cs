using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;

using RemSox.Networking;
using RemSox.Processing;
using RemSox.UI;
using RemSox.UI.GUI.Rendering;
using RemSox.UI.GUI.Windows;

using System.Text;

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

        server.ListenTo("SyncRequest", async _ =>
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

        server.ListenTo("MouseMove", async (payload) =>
        {
            (int X, int Y) = DeserializeMouseMove(payload);
            WindowManager.EnqueueMouseEvent(MouseEvent.Move(X, Y));
        });

        server.ListenTo("MouseDown", async (payload) =>
        {
            (int X, int Y, string Button) = DeserializeMouseButton(payload);
            WindowManager.EnqueueMouseEvent(MouseEvent.ButtonDown(X, Y, ParseButton(Button)));
        });

        server.ListenTo("MouseUp", async (payload) =>
        {
            (int X, int Y, string Button) = DeserializeMouseButton(payload);
            WindowManager.EnqueueMouseEvent(MouseEvent.ButtonUp(X, Y, ParseButton(Button)));
        });

        server.ListenTo("MouseWheel", async (payload) =>
        {
            (int X, int Y, int Delta) = DeserializeMouseWheel(payload);
            WindowManager.EnqueueMouseEvent(MouseEvent.Wheel(X, Y, Delta));
        });

        server.ListenTo("KeyEvent", async (payload) =>
        {
            (int Key, string KeyChar, bool Shift, bool Alt, bool Control, bool Pressed) = DeserializeKeyEvent(payload);
            ConsoleKeyEx key = (ConsoleKeyEx)Key;
            char keyChar = KeyChar.Length > 0 ? KeyChar[0] : '\0';

            KeyEvent keyEvent = new(keyChar, key, Shift, Alt, Control, Pressed ? KeyEvent.KeyEventType.Make : KeyEvent.KeyEventType.Break);
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

    private static byte[] SerializeMouseMove(int x, int y)
    {
        byte[] data = new byte[8];
        WriteInt32(data, 0, x);
        WriteInt32(data, 4, y);
        return data;
    }

    private static (int X, int Y) DeserializeMouseMove(byte[] data)
    {
        return (ReadInt32(data, 0), ReadInt32(data, 4));
    }

    private static byte[] SerializeMouseButton(int x, int y, string button)
    {
        byte[] buttonBytes = Encoding.UTF8.GetBytes(button);
        byte[] data = new byte[8 + 4 + buttonBytes.Length];
        WriteInt32(data, 0, x);
        WriteInt32(data, 4, y);
        WriteInt32(data, 8, buttonBytes.Length);
        buttonBytes.CopyTo(data, 12);
        return data;
    }

    private static (int X, int Y, string Button) DeserializeMouseButton(byte[] data)
    {
        int x = ReadInt32(data, 0);
        int y = ReadInt32(data, 4);
        int len = ReadInt32(data, 8);
        string button = Encoding.UTF8.GetString(data, 12, len);
        return (x, y, button);
    }

    private static byte[] SerializeMouseWheel(int x, int y, int delta)
    {
        byte[] data = new byte[12];
        WriteInt32(data, 0, x);
        WriteInt32(data, 4, y);
        WriteInt32(data, 8, delta);
        return data;
    }

    private static (int X, int Y, int Delta) DeserializeMouseWheel(byte[] data)
    {
        return (ReadInt32(data, 0), ReadInt32(data, 4), ReadInt32(data, 8));
    }

    private static byte[] SerializeKeyEvent(int key, string keyChar, bool shift, bool alt, bool control, bool pressed)
    {
        byte[] charBytes = Encoding.UTF8.GetBytes(keyChar);
        byte[] data = new byte[4 + 4 + charBytes.Length + 4];
        WriteInt32(data, 0, key);
        WriteInt32(data, 4, charBytes.Length);
        charBytes.CopyTo(data, 8);
        int offset = 8 + charBytes.Length;
        data[offset++] = shift ? (byte)1 : (byte)0;
        data[offset++] = alt ? (byte)1 : (byte)0;
        data[offset++] = control ? (byte)1 : (byte)0;
        data[offset] = pressed ? (byte)1 : (byte)0;
        return data;
    }

    private static (int Key, string KeyChar, bool Shift, bool Alt, bool Control, bool Pressed) DeserializeKeyEvent(byte[] data)
    {
        int key = ReadInt32(data, 0);
        int charLen = ReadInt32(data, 4);
        string keyChar = Encoding.UTF8.GetString(data, 8, charLen);
        int offset = 8 + charLen;
        bool shift = data[offset] != 0;
        bool alt = data[offset + 1] != 0;
        bool control = data[offset + 2] != 0;
        bool pressed = data[offset + 3] != 0;
        return (key, keyChar, shift, alt, control, pressed);
    }

    private static void WriteInt32(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static int ReadInt32(byte[] data, int offset)
    {
        return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
    }
}
