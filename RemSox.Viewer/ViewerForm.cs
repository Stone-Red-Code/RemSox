using System.Drawing.Drawing2D;
using System.Net.Sockets;

using RemSox.Shared.Networking;
using RemSox.Shared.UI.GUI.Rendering;

namespace RemSox.Viewer;

/// <summary>
/// Shows the remote desktop scaled to fit the window and forwards mouse and keyboard input.
/// </summary>
internal sealed class ViewerForm : Form
{
    private const int FrameIntervalMs = 16;
    private const int MouseMoveIntervalMs = 33;
    private const int ReconnectIntervalMs = 2000;
    private const int WheelNotch = 120;

    // QEMU's port forwarding accepts the TCP connection even when nothing in the VM listens, so a
    // connection only counts once the server answers the sync. Silent connections are retried.
    private const int ServerResponseTimeoutMs = 3000;

    private const string WaitingMessage = "Waiting for the RemSox remote desktop server...\nRun \"rdp {0}\" in RemSox.";

    private readonly string host;
    private readonly int port;
    private readonly RemoteScreen screen = new();
    private readonly ScreenView view;
    private readonly System.Windows.Forms.Timer frameTimer = new() { Interval = FrameIntervalMs };
    private readonly System.Windows.Forms.Timer reconnectTimer = new() { Interval = ReconnectIntervalMs };
    private readonly System.Windows.Forms.Timer responseTimer = new() { Interval = ServerResponseTimeoutMs };

    private TcpRpcClient? client;
    private volatile bool screenDirty = true;
    private volatile bool serverResponded;
    private bool connecting;
    private bool sizedToRemote;

    private Point? pendingMouseMove;
    private Point lastSentMouse = new(-1, -1);
    private DateTime lastMouseMoveSent = DateTime.MinValue;
    private RemoteKey lastKeyDown = RemoteKey.NoName;

    public ViewerForm(string host, int port)
    {
        this.host = host;
        this.port = port;

        Text = $"RemSox Viewer - {host}:{port}";
        ClientSize = new Size(1024, 640);
        BackColor = Color.Black;
        KeyPreview = false;

        view = new ScreenView(screen) { Dock = DockStyle.Fill, StatusMessage = string.Format(WaitingMessage, port) };
        Controls.Add(view);

        view.MouseMove += (_, e) => QueueMouseMove(e.Location);
        view.MouseDown += (_, e) => SendMouseButton(RemoteInputProtocol.MouseDown, e);
        view.MouseUp += (_, e) => SendMouseButton(RemoteInputProtocol.MouseUp, e);
        view.MouseWheel += (_, e) => SendMouseWheel(e);
        view.KeyDown += OnViewKeyDown;
        view.KeyPress += OnViewKeyPress;

        screen.ScreenSizeChanged += () => BeginInvoke(FitWindowToRemote);

        frameTimer.Tick += (_, _) => OnFrame();
        reconnectTimer.Tick += (_, _) => Connect();
        responseTimer.Tick += (_, _) => OnServerResponseTimeout();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        frameTimer.Start();
        view.Focus();
        Connect();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        frameTimer.Stop();
        reconnectTimer.Stop();
        responseTimer.Stop();
        DisconnectClient();
        screen.Dispose();
        base.OnFormClosed(e);
    }

    // --- Connection ---

    private void Connect()
    {
        if (connecting || client?.IsConnected == true)
        {
            return;
        }

        connecting = true;
        reconnectTimer.Stop();
        SetStatus("connecting...");

        TcpRpcClient newClient = new();
        newClient.ListenTo(RemoteInputProtocol.RenderBatch, payload =>
        {
            foreach (byte[] command in RemoteInputProtocol.DeserializeRenderBatch(payload))
            {
                ApplyRenderCommand(command);
            }

            return Task.CompletedTask;
        });
        newClient.Disconnected += () => BeginInvoke(OnDisconnected);

        _ = Task.Run(() =>
        {
            try
            {
                newClient.Connect(host, port);
                newClient.Send(RemoteInputProtocol.SyncRequest, []);
                BeginInvoke(() => OnConnected(newClient));
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                BeginInvoke(() => OnConnectFailed(ex.Message));
            }
        });
    }

    /// <summary>Applies one serialized render command. Runs on the receive thread.</summary>
    private void ApplyRenderCommand(byte[] payload)
    {
        screen.Apply(RenderCommand.FromBytes(payload));
        screenDirty = true;

        if (!serverResponded)
        {
            serverResponded = true;
            BeginInvoke(OnServerResponded);
        }
    }

    private void OnConnected(TcpRpcClient newClient)
    {
        connecting = false;
        client = newClient;
        lastSentMouse = new Point(-1, -1);

        if (!serverResponded)
        {
            SetStatus("waiting for server...");
            responseTimer.Start();
        }
    }

    private void OnServerResponded()
    {
        responseTimer.Stop();
        view.StatusMessage = null;
        view.Invalidate();
        SetStatus("connected");
    }

    private void OnServerResponseTimeout()
    {
        responseTimer.Stop();

        if (!serverResponded)
        {
            // Nothing answered behind the port forward; drop the connection and try again.
            client?.Disconnect();
        }
    }

    private void OnConnectFailed(string message)
    {
        connecting = false;
        SetStatus($"connection failed ({message}), retrying...");
        reconnectTimer.Start();
    }

    private void OnDisconnected()
    {
        if (IsDisposed)
        {
            return;
        }

        bool wasConnected = serverResponded;

        client = null;
        serverResponded = false;
        responseTimer.Stop();
        screen.Reset();
        screenDirty = true;
        view.StatusMessage = string.Format(WaitingMessage, port);
        SetStatus(wasConnected ? "disconnected, retrying..." : "waiting for server...");
        reconnectTimer.Start();
    }

    private void DisconnectClient()
    {
        client?.Disconnect();
        client = null;
    }

    private void Send(string type, byte[] payload)
    {
        TcpRpcClient? current = client;
        if (current is null || !serverResponded)
        {
            return;
        }

        try
        {
            current.Send(type, payload);
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException or ObjectDisposedException)
        {
            // The receive loop notices the broken connection and raises Disconnected.
        }
    }

    private void SetStatus(string status)
    {
        Text = $"RemSox Viewer - {host}:{port} ({status})";
    }

    // --- Rendering ---

    private void OnFrame()
    {
        FlushMouseMove(force: false);

        if (screenDirty)
        {
            screenDirty = false;
            view.Invalidate();
        }
    }

    private void FitWindowToRemote()
    {
        view.Invalidate();

        if (sizedToRemote)
        {
            return;
        }

        // Show the desktop 1:1 the first time, as long as it fits on this monitor.
        sizedToRemote = true;
        Rectangle workArea = Screen.FromControl(this).WorkingArea;
        Size frame = Size - ClientSize;
        Size target = screen.ScreenSize;

        if (target.Width + frame.Width <= workArea.Width && target.Height + frame.Height <= workArea.Height)
        {
            ClientSize = target;
            Location = new Point(
                workArea.Left + ((workArea.Width - Width) / 2),
                workArea.Top + ((workArea.Height - Height) / 2));
        }
    }

    // --- Mouse input ---

    private void QueueMouseMove(Point clientPoint)
    {
        pendingMouseMove = view.ToRemote(clientPoint);
    }

    /// <summary>Sends the latest pointer position, at most every <see cref="MouseMoveIntervalMs"/> unless forced.</summary>
    private void FlushMouseMove(bool force)
    {
        if (pendingMouseMove is not Point position || position == lastSentMouse)
        {
            return;
        }

        if (!force && (DateTime.UtcNow - lastMouseMoveSent).TotalMilliseconds < MouseMoveIntervalMs)
        {
            return;
        }

        Send(RemoteInputProtocol.MouseMove, RemoteInputProtocol.SerializeMouseMove(position.X, position.Y));
        lastSentMouse = position;
        lastMouseMoveSent = DateTime.UtcNow;
        pendingMouseMove = null;
    }

    private void SendMouseButton(string type, MouseEventArgs e)
    {
        string? button = e.Button switch
        {
            MouseButtons.Left => "Left",
            MouseButtons.Right => "Right",
            MouseButtons.Middle => "Middle",
            _ => null
        };

        if (button is null)
        {
            return;
        }

        view.Focus();

        // The click must land where the pointer is, so send any pending move first.
        Point position = view.ToRemote(e.Location);
        pendingMouseMove = position;
        FlushMouseMove(force: true);

        Send(type, RemoteInputProtocol.SerializeMouseButton(position.X, position.Y, button));
    }

    private void SendMouseWheel(MouseEventArgs e)
    {
        int notches = e.Delta / WheelNotch;
        if (notches == 0)
        {
            return;
        }

        Point position = view.ToRemote(e.Location);

        // Windows reports wheel-up as positive; the kernel follows the PS/2 convention (down is positive).
        Send(RemoteInputProtocol.MouseWheel, RemoteInputProtocol.SerializeMouseWheel(position.X, position.Y, -notches));
    }

    // --- Keyboard input ---

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        lastKeyDown = RemoteKeyMap.Map(e.KeyCode);

        if (RemoteKeyMap.TryGetCommandChar(e.KeyCode, out char keyChar))
        {
            SendKey(lastKeyDown, keyChar);

            // Enter, Backspace, Tab and Escape would otherwise also raise KeyPress.
            e.SuppressKeyPress = true;
            e.Handled = true;
        }
    }

    private void OnViewKeyPress(object? sender, KeyPressEventArgs e)
    {
        SendKey(lastKeyDown, e.KeyChar);
        e.Handled = true;
    }

    private void SendKey(RemoteKey key, char keyChar)
    {
        Keys modifiers = ModifierKeys;
        string text = keyChar == '\0' ? string.Empty : keyChar.ToString();

        Send(RemoteInputProtocol.KeyEvent, RemoteInputProtocol.SerializeKeyEvent(
            (int)key,
            text,
            modifiers.HasFlag(Keys.Shift),
            modifiers.HasFlag(Keys.Alt),
            modifiers.HasFlag(Keys.Control),
            pressed: true));
    }

    /// <summary>
    /// Paints the remote desktop scaled to fit (keeping its aspect ratio) and maps local points back.
    /// </summary>
    private sealed class ScreenView : Control
    {
        private readonly RemoteScreen screen;

        /// <summary>Shown instead of the desktop while not connected to a server.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string? StatusMessage { get; set; }

        public ScreenView(RemoteScreen screen)
        {
            this.screen = screen;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Cross;
        }

        public Point ToRemote(Point clientPoint)
        {
            (float scale, PointF offset) = GetTransform();
            Size remote = screen.ScreenSize;

            int x = (int)((clientPoint.X - offset.X) / scale);
            int y = (int)((clientPoint.Y - offset.Y) / scale);

            return new Point(Math.Clamp(x, 0, remote.Width - 1), Math.Clamp(y, 0, remote.Height - 1));
        }

        protected override bool IsInputKey(Keys keyData)
        {
            // Arrows, Tab and friends belong to the remote desktop, not to focus navigation.
            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.Black);

            if (StatusMessage is not null)
            {
                TextRenderer.DrawText(g, StatusMessage, Font, ClientRectangle, Color.Gainsboro,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            (float scale, PointF offset) = GetTransform();
            g.InterpolationMode = scale == 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.TranslateTransform(offset.X, offset.Y);
            g.ScaleTransform(scale, scale);

            Size remote = screen.ScreenSize;
            g.SetClip(new Rectangle(Point.Empty, remote));
            screen.Paint(g);
        }

        private (float Scale, PointF Offset) GetTransform()
        {
            Size remote = screen.ScreenSize;
            float scale = Math.Min((float)ClientSize.Width / remote.Width, (float)ClientSize.Height / remote.Height);

            if (scale <= 0 || float.IsNaN(scale))
            {
                scale = 1f;
            }

            return (scale, new PointF((ClientSize.Width - (remote.Width * scale)) / 2f, (ClientSize.Height - (remote.Height * scale)) / 2f));
        }
    }
}
