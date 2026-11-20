using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace RemSox.Shared.Networking;

public sealed class TcpRpcServer(IPacketCrypto? crypto = null) : TcpRpcBase(crypto)
{
    private readonly ConcurrentDictionary<TcpConnection, byte> connections = new();
    private TcpListener? listener;
    private CancellationTokenSource? cts;

    /// <summary>
    /// Accepts clients on <paramref name="port"/> until stopped, blocking the calling thread. Each
    /// client is served on a thread of its own, so any number of them can be connected at once.
    /// </summary>
    public void StartAsync(int port, CancellationToken token = default)
    {
        cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        try
        {
            while (!cts.IsCancellationRequested)
            {
                TcpClient client = listener.AcceptTcpClient();
                if (cts.IsCancellationRequested)
                {
                    client.Dispose();
                    break;
                }

                string ep = client.Client.RemoteEndPoint?.ToString() ?? "";
                Log($"Client connected: {ep}");

                TcpConnection conn = new(client);
                _ = connections.TryAdd(conn, 0);

                CancellationToken connectionToken = cts.Token;
                Thread thread = new(() => HandleConnection(conn, connectionToken))
                {
                    IsBackground = true,
                    Name = "TcpRpc " + ep,
                };
                thread.Start();
            }
        }
        catch (Exception) when (cts.IsCancellationRequested)
        {
            // Stop() closed the listener under a pending accept.
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    public void Stop()
    {
        cts?.Cancel();
        listener?.Stop();

        foreach (TcpConnection c in connections.Keys)
        {
            c.Dispose();
        }

        connections.Clear();
    }

    public bool HasConnections => !connections.IsEmpty;

    /// <summary>
    /// Pushes a one-way message to every client. It carries no request id, since no reply is expected.
    /// </summary>
    public void SendRawToAll(string type, byte[] payload)
    {
        TcpMessage message = new()
        {
            Type = type,
            RequestId = string.Empty,
            Payload = payload
        };

        foreach (TcpConnection conn in connections.Keys)
        {
            // A client that went away must not keep the others from getting the message; its
            // receive thread notices the closed socket and removes it. (Cosmos' TCP send throws a
            // plain Exception, so no narrower type can be caught.)
            try
            {
                SendRaw(conn, message);
            }
            catch (Exception)
            {
                _ = connections.TryRemove(conn, out _);
            }
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

    protected override void OnConnectionClosed(TcpConnection conn)
    {
        _ = connections.TryRemove(conn, out _);

        Log("Client disconnected.");
    }
}
