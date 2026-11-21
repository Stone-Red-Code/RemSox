using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;

using RemSox.Kernel.Accounts;
using RemSox.Kernel.Processing;
using RemSox.Kernel.Programs;
using RemSox.Kernel.UI.GUI.Rendering;
using RemSox.Kernel.UI.GUI.Windows;
using RemSox.Shared.Cryptography;
using RemSox.Shared.Networking;
using RemSox.Shared.UI;
using RemSox.Shared.UI.GUI.Rendering;

namespace RemSox.Kernel.Processes;

/// <summary>
/// The remote desktop server: shows each viewer the screen of its own remote desktop session and
/// passes its mouse and keyboard input to that screen alone.
/// </summary>
/// <remarks>
/// <para>
/// A service: it runs as the system. Viewers log in with an account's name and password, and
/// everything after that is encrypted (<see cref="RpcHandshake"/>). Each account has one desktop
/// session: a login joins it wherever it runs, taking it from the machine if it is there (the
/// machine shows its login screen meanwhile, and logging in there takes it back, disconnecting
/// the viewer), or else starts a remote desktop session with a desktop. Different users work at
/// once, each on their own <see cref="Screen"/>. Closing the viewer does not end the session
/// ("Log out" in the start menu does, and disconnects its viewers, as locking it does).
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

    private readonly Dictionary<TcpConnection, Viewer> viewers = [];

    private TcpRpcServer? server;

    /// <summary>Gets the port the server listens on.</summary>
    public int Port { get; private set; }

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
        server.ClientLoggedOut += OnViewerLoggedOut;
        LogonSessions.Ended += OnSessionEnded;
        LogonSessions.Locked += OnSessionLocked;

        server.ListenTo(RemoteInputProtocol.SyncRequest, (connection, _) =>
        {
            if (viewers.TryGetValue(connection, out Viewer? viewer))
            {
                Canvas canvas = Canvas.GetFullScreen();
                viewer.Source.Render([new RenderCommand
                {
                    Type = RenderCommandType.ScreenInfo,
                    WindowId = 0,
                    ElementId = 0,
                    Properties = new()
                    {
                        ["Width"] = (int)canvas.Mode.Width,
                        ["Height"] = (int)canvas.Mode.Height,
                    },
                }]);
                viewer.Screen.InvalidateAll();
            }

            return Task.CompletedTask;
        });

        RegisterInputHandlers();

        server.Start(port);

        Logger.Log($"Remote desktop server started on port {port}.", Logging.LogSeverity.Info);
    }

    internal override void Tick()
    {
        foreach ((TcpConnection connection, Viewer viewer) in viewers.Where(pair => pair.Value.Screen.Session.IsAtMachine).ToList())
        {
            EndViewer(connection, $"{viewer.Screen.Session.Account.Name} logged in at the machine, which took this session back.");
        }

        foreach (Viewer viewer in viewers.Values)
        {
            viewer.Source.Flush();
        }

        server?.Poll(WriteBudgetMs);
    }

    internal override void Stop()
    {
        LogonSessions.Ended -= OnSessionEnded;
        LogonSessions.Locked -= OnSessionLocked;
        server?.Stop();
        Logger.Log("Remote desktop server stopped.", Logging.LogSeverity.Info);
    }

    public PasswordHash? FindPassword(string user)
    {
        return AccountStore.Find(user) is { } account ? AccountStore.GetPasswordHash(account) : null;
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
    /// Shows a viewer that logged in its account's desktop session, moving it off the machine if
    /// it is there, or else a new session with a new desktop; a viewer that gets neither is
    /// disconnected.
    /// </summary>
    private void OnViewerLoggedIn(TcpConnection connection, string user, string address)
    {
        if (AccountStore.Find(user) is not { } account || FindOrStartSession(account, address) is not { } session)
        {
            EndViewer(connection, "No desktop could be started; the system log says why.");
            return;
        }

        if (session.IsAtMachine)
        {
            LogonSessions.MoveToMachine(null);
        }

        Viewer viewer = new(new NetworkRenderSource(server!, connection), Screen.Of(session));
        viewers[connection] = viewer;
        viewer.Screen.AddRenderSource(viewer.Source);
        session.Viewers++;
    }

    private LogonSession? FindOrStartSession(Account account, string address)
    {
        if (DesktopProcess.FindSession(account) is { } running)
        {
            return running;
        }

        LogonSession session = LogonSessions.Start(account, LogonKind.RemoteDesktop, address);
        ProgramStart start = ProgramRegistry.Find(BuiltInPrograms.DesktopName) is { } desktop
            ? ProgramRegistry.Start(desktop, [], account.HomeFolder, new Caller(session, Elevated: false))
            : ProgramStart.Fail("There is no desktop program.");

        if (start.Error is not null)
        {
            Logger.Log($"Could not start a desktop for {account.Name}: {start.Error}", Logging.LogSeverity.Error);
            LogonSessions.End(session);
            return null;
        }

        return session;
    }

    private void OnViewerLoggedOut(TcpConnection connection)
    {
        if (viewers.Remove(connection, out Viewer? viewer))
        {
            viewer.Screen.RemoveRenderSource(viewer.Source);
            viewer.Screen.Session.Viewers--;
        }
    }

    private void OnSessionEnded(LogonSession session)
    {
        EndViewers(session, "The session was logged out.");
    }

    private void OnSessionLocked(LogonSession session)
    {
        EndViewers(session, "The session was locked. Log in to unlock it.");
    }

    private void EndViewers(LogonSession session, string reason)
    {
        foreach (TcpConnection connection in viewers.Where(pair => pair.Value.Screen.Session == session).Select(pair => pair.Key).ToList())
        {
            EndViewer(connection, reason);
        }
    }

    /// <summary>
    /// Disconnects a viewer on purpose, telling it why first (<see cref="RemoteInputProtocol.SessionEnded"/>)
    /// so it does not reconnect by itself. It stops showing its screen at once.
    /// </summary>
    private void EndViewer(TcpConnection connection, string reason)
    {
        OnViewerLoggedOut(connection);
        server?.Push(connection, RemoteInputProtocol.SessionEnded, System.Text.Encoding.UTF8.GetBytes(reason));
        server?.Disconnect(connection);
    }

    private void RegisterInputHandlers()
    {
        if (server is null)
        {
            return;
        }

        server.ListenTo(RemoteInputProtocol.MouseMove, (connection, payload) =>
        {
            (int x, int y) = RemoteInputProtocol.DeserializeMouseMove(payload);
            Find(connection)?.EnqueueMouseEvent(MouseEvent.Move(x, y));
            return Task.CompletedTask;
        });

        server.ListenTo(RemoteInputProtocol.MouseDown, (connection, payload) =>
        {
            (int x, int y, string button) = RemoteInputProtocol.DeserializeMouseButton(payload);
            Find(connection)?.EnqueueMouseEvent(MouseEvent.ButtonDown(x, y, ParseButton(button)));
            return Task.CompletedTask;
        });

        server.ListenTo(RemoteInputProtocol.MouseUp, (connection, payload) =>
        {
            (int x, int y, string button) = RemoteInputProtocol.DeserializeMouseButton(payload);
            Find(connection)?.EnqueueMouseEvent(MouseEvent.ButtonUp(x, y, ParseButton(button)));
            return Task.CompletedTask;
        });

        server.ListenTo(RemoteInputProtocol.MouseWheel, (connection, payload) =>
        {
            (int x, int y, int delta) = RemoteInputProtocol.DeserializeMouseWheel(payload);
            Find(connection)?.EnqueueMouseEvent(MouseEvent.Wheel(x, y, delta));
            return Task.CompletedTask;
        });

        server.ListenTo(RemoteInputProtocol.KeyEvent, (connection, payload) =>
        {
            (int key, string keyChar, bool shift, bool alt, bool control, bool pressed) = RemoteInputProtocol.DeserializeKeyEvent(payload);

            // The local keyboard only queues presses, and windows don't check the event type,
            // so a release would be handled as a second press.
            if (!pressed)
            {
                return Task.CompletedTask;
            }

            KeyEvent keyEvent = new(keyChar.Length > 0 ? keyChar[0] : '\0', (Key)key, shift, alt, control, KeyEvent.KeyEventType.Make);
            Find(connection)?.EnqueueKeyEvent(keyEvent);
            return Task.CompletedTask;
        });
    }

    /// <summary>Returns the screen a viewer's input goes to, or null for a connection that has none.</summary>
    private Screen? Find(TcpConnection connection)
    {
        return viewers.TryGetValue(connection, out Viewer? viewer) ? viewer.Screen : null;
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

    /// <summary>One logged-in viewer: where its render commands go, and the screen it shows.</summary>
    private sealed record Viewer(NetworkRenderSource Source, Screen Screen);
}
