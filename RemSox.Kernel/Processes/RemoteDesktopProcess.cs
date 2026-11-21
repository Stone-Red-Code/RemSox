using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;

using RemSox.Kernel.Accounts;
using RemSox.Shared.Cryptography;
using RemSox.Shared.Networking;
using RemSox.Kernel.Processing;
using RemSox.Kernel.Programs;
using RemSox.Shared.UI;
using RemSox.Shared.UI.GUI.Rendering;
using RemSox.Kernel.UI.GUI.Rendering;
using RemSox.Kernel.UI.GUI.Windows;

namespace RemSox.Kernel.Processes;

/// <summary>
/// The remote desktop server: sends the desktop's render commands to viewers and passes their
/// mouse and keyboard input to the window manager.
/// </summary>
/// <remarks>
/// <para>
/// A service: it runs as the system. Viewers log in with an account's name and password, and
/// everything after that is encrypted (<see cref="RpcHandshake"/>). There is one screen, so one
/// desktop: a login with no desktop running starts one in a new remote desktop session for that
/// account; a login of the account whose desktop runs joins it; anyone else is refused. Closing
/// the viewer does not end the session ("Log out" in the start menu does), and while a viewer is
/// connected the local screen is locked (see <see cref="DesktopProcess"/>).
/// </para>
/// <para>
/// It has no threads: each tick polls the server, so viewers' input is handled on the main loop
/// and an idle connection costs nothing.
/// </para>
/// </remarks>
internal sealed class RemoteDesktopProcess() : Process("Remote Desktop Server"), IRpcLogins
{
    // How long a tick may spend writing to viewers. Writes block for a round trip per segment,
    // so this bounds what a connected viewer costs the rest of the system.
    private const int WriteBudgetMs = 8;

    private TcpRpcServer? server;
    private NetworkRenderSource? networkSource;

    /// <summary>Gets the port the server listens on.</summary>
    public int Port { get; private set; }

    /// <summary>Gets whether a viewer is logged in, and so sees the desktop.</summary>
    public bool HasViewers => server?.HasConnections == true;

    /// <param name="args">The port, already checked by the <c>rdp</c> program's <c>Prepare</c>.</param>
    internal override void Start(string[] args)
    {
        int port = int.Parse(args[0]);
        Port = port;
        ListeningPorts.Claim(port, this);
        server = new TcpRpcServer(this)
        {
            Log = message => Logger.Log(message, Logging.LogSeverity.Info),
        };
        server.ClientLoggedIn += OnViewerLoggedIn;
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

        server.Start(port);

        WindowManager.AddRenderSource(networkSource);

        Logger.Log($"Remote desktop server started on port {port}.", Logging.LogSeverity.Info);
    }

    internal override void Tick()
    {
        networkSource?.Flush();
        server?.Poll(WriteBudgetMs);
    }

    internal override void Stop()
    {
        server?.Stop();

        if (networkSource is not null)
        {
            WindowManager.RemoveRenderSource(networkSource);
        }

        Logger.Log("Remote desktop server stopped.", Logging.LogSeverity.Info);
    }

    public PasswordHash? FindPassword(string user)
    {
        return AccountStore.Find(user) is { } account ? AccountStore.GetPasswordHash(account) : null;
    }

    public string? Refuse(string user)
    {
        DesktopProcess? desktop = ProcessManager.GetProcessesOfType<DesktopProcess>().FirstOrDefault();
        return desktop is not null && !string.Equals(desktop.Session.Account.Name, user, StringComparison.OrdinalIgnoreCase)
            ? $"The desktop is in use by {desktop.Session.Account.Name}."
            : null;
    }

    public string? Throttle(string user)
    {
        return LoginThrottle.Refuse(user);
    }

    public void Report(string user, bool rightPassword)
    {
        if (rightPassword)
        {
            LoginThrottle.Succeeded(user);
        }
        else
        {
            LoginThrottle.Failed(user);
        }
    }

    /// <summary>
    /// Gives a viewer that logged in a desktop: the running one (<see cref="Refuse"/> made sure it
    /// is the same account's), or a new one in a new remote desktop session, taking the screen
    /// from the login window or console that had it.
    /// </summary>
    private void OnViewerLoggedIn(string user, string address)
    {
        if (ProcessManager.IsProcessRunning<DesktopProcess>() || AccountStore.Find(user) is not { } account)
        {
            return;
        }

        foreach (LoginWindowProcess login in ProcessManager.GetProcessesOfType<LoginWindowProcess>())
        {
            ProcessManager.StopProcess(login.Id);
        }

        LogonSession session = LogonSessions.Start(account, LogonKind.RemoteDesktop, address);
        ProgramStart start = ProgramRegistry.Find(BuiltInPrograms.DesktopName) is { } desktop
            ? ProgramRegistry.Start(desktop, [], account.HomeFolder, new Caller(session, Elevated: false))
            : ProgramStart.Fail("There is no desktop program.");

        if (start.Error is not null)
        {
            Logger.Log($"Could not start a desktop for {account.Name}: {start.Error}", Logging.LogSeverity.Error);
            LogonSessions.End(session);
        }
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
