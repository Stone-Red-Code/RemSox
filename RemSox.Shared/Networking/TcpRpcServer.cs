using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace RemSox.Shared.Networking;

public class TcpRpcServer(IPacketCrypto? crypto = null) : TcpRpcBase(crypto)
{
    private readonly ConcurrentDictionary<TcpConnection, byte> connections = new();
    private readonly ConcurrentDictionary<string, byte> activeEndpoints = new();
    private TcpListener? listener;
    private CancellationTokenSource? cts;

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
                string ep = client.Client.RemoteEndPoint?.ToString() ?? "";

                if (!activeEndpoints.TryAdd(ep, 0))
                {
                    client.Close();
                    Thread.Sleep(10);
                    continue;
                }

                Console.WriteLine($"Client connected: {ep}");
                TcpConnection conn = new(client);
                _ = connections.TryAdd(conn, 0);

                _ = Task.Run(() => HandleConnection(conn, cts.Token));
            }
        }
        catch (OperationCanceledException)
        {
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

    public void SendRawToAll(string type, byte[] payload)
    {
        foreach (TcpConnection conn in connections.Keys)
        {
            SendRaw(conn, new TcpMessage
            {
                Type = type,
                RequestId = Guid.NewGuid().ToString(),
                Payload = payload
            });
        }
    }

    protected override void OnConnectionClosed(TcpConnection conn)
    {
        _ = connections.TryRemove(conn, out _);

        string ep = conn.Client.Client.RemoteEndPoint?.ToString() ?? "";
        _ = activeEndpoints.TryRemove(ep, out _);

        Console.WriteLine($"Client disconnected: {ep}");
    }
}
