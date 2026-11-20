using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

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
/// </remarks>
public sealed class TcpRpcServer(IPacketCrypto? crypto = null) : TcpRpcBase(crypto)
{
    private const int InitialInboxSize = 16 * 1024;

    private readonly List<Client> clients = [];
    private TcpListener? listener;

    /// <summary>Gets whether any client is connected.</summary>
    public bool HasConnections => clients.Count > 0;

    /// <summary>Gets the most packets any client still has waiting to be written.</summary>
    public int MaxQueuedPackets => clients.Count == 0 ? 0 : clients.Max(c => c.Outgoing.Count);

    /// <summary>Starts listening on <paramref name="port"/>; clients are accepted by <see cref="Poll"/>.</summary>
    public void Start(int port)
    {
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
    }

    /// <summary>Closes every connection and stops listening.</summary>
    public void Stop()
    {
        foreach (Client client in clients)
        {
            client.Connection.Dispose();
        }

        clients.Clear();
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
            TcpConnection connection = new(listener.AcceptTcpClient());
            clients.Add(new Client(connection));
            Log($"Client connected: {connection.Client.Client.RemoteEndPoint}");
        }

        for (int i = clients.Count - 1; i >= 0; i--)
        {
            if (!Receive(clients[i]))
            {
                Close(clients[i]);
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

    /// <summary>Queues a one-way message for every client. It carries no request id, since no reply is expected.</summary>
    public void SendRawToAll(string type, byte[] payload)
    {
        byte[] packet = BuildPacket(new TcpMessage { Type = type, RequestId = string.Empty, Payload = payload });

        foreach (Client client in clients)
        {
            client.Outgoing.Enqueue(packet);
        }
    }

    /// <summary>
    /// Size in bytes of a message frame for <paramref name="type"/> sent by <see cref="SendRawToAll"/>, excluding the payload.
    /// </summary>
    public static int GetPushOverhead(string type)
    {
        // length prefix + type length + type + request id length + payload length
        return 4 + 4 + System.Text.Encoding.UTF8.GetByteCount(type) + 4 + 4;
    }

    /// <summary>Queues <paramref name="msg"/> for its client; the next <see cref="Poll"/> writes it.</summary>
    protected override void SendRaw(TcpConnection conn, TcpMessage msg)
    {
        clients.Find(c => c.Connection == conn)?.Outgoing.Enqueue(BuildPacket(msg));
    }

    protected override void OnConnectionClosed(TcpConnection conn)
    {
        Log("Client disconnected.");
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

                client.InboxLength += read;
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
            if (length is <= 0 or > MaxMessageSize)
            {
                Log($"[TcpRpc] Invalid packet length: {length}");
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

            Dispatch(client.Connection, client.Inbox[(start + 4)..(start + 4 + length)]);
            start += 4 + length;

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

    /// <summary>Writes queued packets until none is left or <paramref name="deadline"/> has passed.</summary>
    /// <returns>False once the connection is gone.</returns>
    private bool Write(Client client, long deadline)
    {
        try
        {
            while (client.Outgoing.Count > 0 && Stopwatch.GetTimestamp() < deadline)
            {
                byte[] packet = client.Outgoing.Dequeue();
                client.Connection.Stream.Write(packet, 0, packet.Length);
            }

            return true;
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
        OnConnectionClosed(client.Connection);
        client.Connection.Dispose();
    }

    private sealed class Client(TcpConnection connection)
    {
        public TcpConnection Connection { get; } = connection;

        /// <summary>Received bytes not yet dispatched, from index 0 to <see cref="InboxLength"/>.</summary>
        public byte[] Inbox = new byte[InitialInboxSize];

        public int InboxLength;

        public Queue<byte[]> Outgoing { get; } = new();
    }
}
