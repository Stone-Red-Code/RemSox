using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

using RemSox.Shared.Cryptography;

namespace RemSox.Shared.Networking;

/// <summary>
/// An RPC server without threads of its own: its owner calls <see cref="Poll"/> from its loop,
/// which accepts clients, reads and dispatches what they sent, and writes queued messages.
/// Handlers therefore run on the owner's thread, and nothing runs while it is not polling.
/// </summary>
/// <remarks>
/// Built for the kernel's single main loop: threads that wait on sockets take CPU time from it
/// under Cosmos's preemptive scheduler even when nothing arrives. Writes still block (Cosmos's
/// TCP send waits for each segment's acknowledgement), so <see cref="Poll"/> writes only for as
/// long as its time budget allows and leaves the rest queued.
/// <para>
/// A client must log in with a user name and password first (<see cref="RpcHandshake"/>), which
/// the owner checks through <see cref="IRpcLogins"/>. Until it has, nothing it sends reaches a
/// handler and nothing is sent to it but the handshake; one that does not log in within
/// <see cref="RpcHandshake.Timeout"/>, or whose login fails, is disconnected.
/// </para>
/// </remarks>
public sealed class TcpRpcServer(IRpcLogins logins) : TcpRpcBase
{
    private const int InitialInboxSize = 16 * 1024;

    /// <summary>
    /// The most clients that may be logging in at once: more are disconnected as soon as they
    /// connect, so nobody can hold many handshakes open to guess passwords in parallel.
    /// </summary>
    public const int MaxPendingClients = 8;

    private readonly List<Client> clients = [];

    // Makes the salts of names without an account; new for every server, never stored.
    private readonly byte[] decoySecret = RandomNumberGenerator.GetBytes(32);

    private TcpListener? listener;

    /// <summary>
    /// Raised during <see cref="Poll"/> when a client has logged in, with its connection (which
    /// identifies it to handlers and <see cref="Push"/>), user name and address.
    /// </summary>
    public event Action<TcpConnection, string, string>? ClientLoggedIn;

    /// <summary>Raised during <see cref="Poll"/>, <see cref="Disconnect"/> or <see cref="Stop"/> when a logged-in client is gone.</summary>
    public event Action<TcpConnection>? ClientLoggedOut;

    /// <summary>Starts listening on <paramref name="port"/>; clients are accepted by <see cref="Poll"/>.</summary>
    public void Start(int port)
    {
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
    }

    /// <summary>Closes every connection and stops listening.</summary>
    public void Stop()
    {
        foreach (Client client in clients.ToList())
        {
            Close(client);
        }

        listener?.Stop();
        listener = null;
    }

    /// <summary>
    /// Accepts waiting clients, dispatches every complete message received (handlers run here, in
    /// arrival order) and writes queued packets for up to <paramref name="writeBudgetMs"/>
    /// milliseconds. Never waits for a client to send anything.
    /// </summary>
    public void Poll(int writeBudgetMs)
    {
        if (listener is null)
        {
            return;
        }

        while (listener.Pending())
        {
            // The client speaks first, with the name it logs in as.
            Client client = new(new TcpConnection(listener.AcceptTcpClient()), RpcHandshake.CreateNonce());
            if (clients.Count(c => !c.LoggedIn) >= MaxPendingClients)
            {
                Log($"Client {client.Address} refused: {MaxPendingClients} clients are already logging in.");
                client.Connection.Dispose();
                continue;
            }

            clients.Add(client);
            Log($"Client connected: {client.Address}");
        }

        long now = Stopwatch.GetTimestamp();
        for (int i = clients.Count - 1; i >= 0; i--)
        {
            Client client = clients[i];
            if (!client.LoggedIn && !client.CloseWhenSent && Stopwatch.GetElapsedTime(client.ConnectedAt, now) > RpcHandshake.Timeout)
            {
                Log($"Client {client.Address} did not log in in time.");
                Close(client);
            }
            else if (!Receive(client))
            {
                Close(client);
            }
        }

        long deadline = Stopwatch.GetTimestamp() + (writeBudgetMs * Stopwatch.Frequency / 1000);
        for (int i = clients.Count - 1; i >= 0; i--)
        {
            if (!Write(clients[i], deadline))
            {
                Close(clients[i]);
            }
        }
    }

    /// <summary>
    /// Queues a one-way message for one logged-in client; nothing happens once it is gone. It
    /// carries no request id, since no reply is expected.
    /// </summary>
    public void Push(TcpConnection connection, string type, byte[] payload)
    {
        SendRaw(connection, new TcpMessage { Type = type, RequestId = string.Empty, Payload = payload });
    }

    /// <summary>Gets how many packets a client still has waiting to be written; 0 once it is gone.</summary>
    public int GetQueuedPackets(TcpConnection connection)
    {
        return clients.Find(c => c.Connection == connection)?.Outgoing.Count ?? 0;
    }

    /// <summary>
    /// Closes one client's connection once what is queued for it has been written (at once if
    /// nothing is), raising <see cref="ClientLoggedOut"/> then. Nothing more is sent to it or
    /// read from it meanwhile.
    /// </summary>
    public void Disconnect(TcpConnection connection)
    {
        if (clients.Find(c => c.Connection == connection) is { } client)
        {
            client.CloseWhenSent = true;
        }
    }

    /// <summary>
    /// Size in bytes of a message frame for <paramref name="type"/> sent by <see cref="Push"/>, excluding the payload.
    /// </summary>
    public static int GetPushOverhead(string type)
    {
        // length prefix + type length + type + request id length + payload length + tag
        return 4 + 4 + System.Text.Encoding.UTF8.GetByteCount(type) + 4 + 4 + AesGcmSession.Overhead;
    }

    /// <summary>Queues <paramref name="msg"/> for its client; the next <see cref="Poll"/> writes it.</summary>
    protected override void SendRaw(TcpConnection conn, TcpMessage msg)
    {
        Client? client = clients.Find(c => c.Connection == conn);
        if (client is { LoggedIn: true, CloseWhenSent: false })
        {
            client.Outgoing.Enqueue(BuildPacket(conn, msg));
        }
    }

    protected override void OnConnectionClosed(TcpConnection conn)
    {
    }

    /// <summary>Reads what the client has sent and dispatches every complete packet.</summary>
    /// <returns>False once the connection is closed or its data cannot be followed.</returns>
    private bool Receive(Client client)
    {
        Socket socket = client.Connection.Client.Client;

        try
        {
            // Readable with nothing to read means the peer closed: Read then returns 0.
            while (socket.Poll(0, SelectMode.SelectRead))
            {
                int free = client.Inbox.Length - client.InboxLength;
                if (free == 0)
                {
                    Array.Resize(ref client.Inbox, client.Inbox.Length * 2);
                    free = client.Inbox.Length - client.InboxLength;
                }

                int read = client.Connection.Stream.Read(client.Inbox, client.InboxLength, free);
                if (read == 0)
                {
                    return false;
                }

                // A rejected client is only waiting for its refusal to be written: ignore it.
                client.InboxLength = client.CloseWhenSent ? 0 : client.InboxLength + read;
            }
        }
        catch (Exception)
        {
            // Cosmos's sockets throw a plain Exception when the connection is gone.
            return false;
        }

        int start = 0;
        while (client.InboxLength - start >= 4)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(client.Inbox.AsSpan(start));
            if (length <= 0 || length > (client.LoggedIn ? MaxMessageSize : RpcHandshake.MaxPacketSize))
            {
                Log($"[TcpRpc] Invalid packet length from {client.Address}: {length}");
                return false;
            }

            if (client.InboxLength - start - 4 < length)
            {
                // Incomplete: make room for the rest if the inbox is too small to hold it.
                if (4 + length > client.Inbox.Length)
                {
                    Array.Resize(ref client.Inbox, 4 + length);
                }

                break;
            }

            byte[] body = client.Inbox[(start + 4)..(start + 4 + length)];
            start += 4 + length;

            if (!client.LoggedIn)
            {
                if (!(client.PasswordKey is null ? Challenge(client, body) : LogIn(client, body)))
                {
                    return false;
                }

                if (client.CloseWhenSent)
                {
                    start = client.InboxLength;
                    break;
                }

                continue;
            }

            try
            {
                Dispatch(client.Connection, body);
            }
            catch (CryptographicException)
            {
                Log($"[TcpRpc] Rejected a forged or replayed packet from {client.Address}.");
                return false;
            }

            if (client.Connection.IsClosed)
            {
                return false;
            }
        }

        if (start > 0)
        {
            Array.Copy(client.Inbox, start, client.Inbox, 0, client.InboxLength - start);
            client.InboxLength -= start;
        }

        return true;
    }

    /// <summary>
    /// Answers a client's hello (the name it logs in as) with the challenge for that account, or
    /// for a made-up one when there is no such account, so the answer looks the same either way.
    /// </summary>
    /// <returns>False when the packet is not a hello, so the client must be closed at once.</returns>
    private bool Challenge(Client client, byte[] packet)
    {
        try
        {
            client.User = RpcHandshake.ReadHello(packet);
        }
        catch (InvalidDataException ex)
        {
            Log($"Client {client.Address} sent no valid hello: {ex.Message}");
            return false;
        }

        PasswordHash? hash = logins.FindPassword(client.User);
        client.IsKnownUser = hash is not null;
        client.PasswordKey = hash is null ? RpcHandshake.CreateDecoyKey(decoySecret, client.User) : RpcHandshake.GetPasswordKey(hash);
        client.Outgoing.Enqueue(Frame(RpcHandshake.CreateChallenge(client.PasswordKey, client.ServerNonce)));
        return true;
    }

    /// <summary>
    /// Checks a client's login packet. Accepted, it switches the connection to encryption;
    /// rejected or refused, it queues the answer and has the client closed once that is written.
    /// </summary>
    /// <returns>False when the packet is not a login, so the client must be closed at once.</returns>
    private bool LogIn(Client client, byte[] packet)
    {
        byte[] clientNonce;
        byte[] clientProof;
        try
        {
            (clientNonce, clientProof) = RpcHandshake.ReadLogin(packet);
        }
        catch (InvalidDataException ex)
        {
            Log($"Client {client.Address} sent no valid login: {ex.Message}");
            return false;
        }

        string user = client.User!;
        if (logins.Throttle(user) is { } wait)
        {
            Log($"Client {client.Address} may not try to log in as {user} now: {wait}");
            client.Outgoing.Enqueue(Frame(RpcHandshake.CreateRefusal(wait)));
            client.CloseWhenSent = true;
            return true;
        }

        RpcHandshake.SessionKeys keys = RpcHandshake.DeriveSessionKeys(user, client.PasswordKey!, client.ServerNonce, clientNonce);

        bool rightPassword = client.IsKnownUser && RpcHandshake.IsValidClientProof(clientProof, keys);
        logins.Report(user, rightPassword);
        if (!rightPassword)
        {
            Log($"Client {client.Address} gave a wrong name or password ({user}).");
            client.Outgoing.Enqueue(Frame(RpcHandshake.CreateResult(null)));
            client.CloseWhenSent = true;
            return true;
        }

        // The result goes out unencrypted; everything after it is encrypted.
        client.Outgoing.Enqueue(Frame(RpcHandshake.CreateResult(keys)));
        client.Connection.Crypto = new AesGcmSession(keys.ServerToClient, keys.ClientToServer);
        Log($"Client {client.Address} logged in as {user}.");
        ClientLoggedIn?.Invoke(client.Connection, user, client.Address);
        return true;
    }

    /// <summary>Writes queued packets until none is left or <paramref name="deadline"/> has passed.</summary>
    /// <returns>False once the connection is gone, or a rejected client has been told so.</returns>
    private bool Write(Client client, long deadline)
    {
        try
        {
            while (client.Outgoing.Count > 0 && Stopwatch.GetTimestamp() < deadline)
            {
                byte[] packet = client.Outgoing.Dequeue();
                client.Connection.Stream.Write(packet, 0, packet.Length);
            }

            return !(client.CloseWhenSent && client.Outgoing.Count == 0);
        }
        catch (Exception)
        {
            // Cosmos's TCP send throws a plain Exception when the connection is gone.
            return false;
        }
    }

    private void Close(Client client)
    {
        _ = clients.Remove(client);
        client.Connection.Dispose();
        Log($"Client {client.Address} disconnected.");

        if (client.LoggedIn)
        {
            ClientLoggedOut?.Invoke(client.Connection);
        }
    }

    private sealed class Client(TcpConnection connection, byte[] serverNonce)
    {
        public TcpConnection Connection { get; } = connection;

        /// <summary>Gets the remote end point, for the log; kept since a closed socket no longer has it.</summary>
        public string Address { get; } = connection.Client.Client.RemoteEndPoint?.ToString() ?? "unknown";

        public byte[] ServerNonce { get; } = serverNonce;

        public long ConnectedAt { get; } = Stopwatch.GetTimestamp();

        /// <summary>Gets or sets the name the client logs in as, once its hello arrived.</summary>
        public string? User { get; set; }

        /// <summary>Gets or sets the key the client must prove it knows, once its hello arrived (a decoy for an unknown name).</summary>
        public RpcHandshake.PasswordKey? PasswordKey { get; set; }

        /// <summary>Gets or sets whether <see cref="User"/> names an account; a decoy key never logs in.</summary>
        public bool IsKnownUser { get; set; }

        /// <summary>Gets whether the client gave the right password; only then are its messages handled.</summary>
        public bool LoggedIn => Connection.Crypto is not null;

        /// <summary>Set when the login was rejected: the client is closed once the refusal is written.</summary>
        public bool CloseWhenSent { get; set; }

        /// <summary>Received bytes not yet dispatched, from index 0 to <see cref="InboxLength"/>.</summary>
        public byte[] Inbox = new byte[InitialInboxSize];

        public int InboxLength;

        public Queue<byte[]> Outgoing { get; } = new();
    }
}
