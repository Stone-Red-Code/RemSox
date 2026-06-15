using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace RemSox.Networking;

public class TcpRpcServer(IPacketCrypto? crypto = null) : TcpRpcBase(crypto)
{
    // Fix: Using ConcurrentDictionary to prevent collection errors during structural additions/prunings
    private readonly ConcurrentDictionary<TcpConnection, byte> connections = new();
    private TcpListener? listener;
    private CancellationTokenSource? cts;

    public async Task StartAsync(int port, CancellationToken token = default)
    {
        cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        try
        {
            while (!cts.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cts.Token);

                TcpConnection conn = new(client);
                _ = connections.TryAdd(conn, 0);

                _ = HandleConnection(conn, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected shutdown scenario
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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Handled via library constraints")]
    public void RespondTo<TReq, TRes>(string type, Func<TReq, Task<TRes>> handler)
    {
        handlers[type] = async (conn, msg) =>
        {
            TReq req = JsonSerializer.Deserialize<TReq>(msg.Payload)!;
            TRes res = await handler(req);

            await SendRaw(conn, new TcpMessage
            {
                Type = type,
                RequestId = msg.RequestId,
                Payload = JsonSerializer.SerializeToUtf8Bytes(res)
            });
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Handled via library constraints")]
    public Task SendToAll<T>(string type, T data)
    {
        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(data);
        List<Task> sendTasks = [];

        foreach (TcpConnection conn in connections.Keys)
        {
            sendTasks.Add(SendRaw(conn, new TcpMessage
            {
                Type = type,
                RequestId = Guid.NewGuid().ToString(),
                Payload = payloadBytes
            }));
        }

        return Task.WhenAll(sendTasks);
    }

    /// <summary> Broadcasts raw binary payload to all connected clients. </summary>
    public Task SendRawToAll(string type, byte[] payload)
    {
        List<Task> sendTasks = [];

        foreach (TcpConnection conn in connections.Keys)
        {
            sendTasks.Add(SendRaw(conn, new TcpMessage
            {
                Type = type,
                RequestId = Guid.NewGuid().ToString(),
                Payload = payload
            }));
        }

        return Task.WhenAll(sendTasks);
    }

    protected override void OnConnectionClosed(TcpConnection conn)
    {
        _ = connections.TryRemove(conn, out _);
    }
}