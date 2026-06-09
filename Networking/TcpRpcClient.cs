using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Text.Json;

namespace RemSox.Networking;

public class TcpRpcClient(IPacketCrypto? crypto = null) : TcpRpcBase(crypto)
{
    private TcpConnection? connection;

    public async Task ConnectAsync(string host, int port)
    {
        TcpClient client = new();
        await client.ConnectAsync(host, port);

        connection = new TcpConnection(client);

        // Run network monitoring task loop background-detached
        _ = HandleConnection(connection);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Handled via library constraints")]
    public async Task<TRes> RequestAsync<TReq, TRes>(string type, TReq request, TimeSpan timeout = default)
    {
        if (connection is null)
        {
            throw new InvalidOperationException("Client not connected.");
        }

        if (timeout == default)
        {
            timeout = TimeSpan.FromSeconds(30); // Default fallback timeout
        }

        string requestId = Guid.NewGuid().ToString();
        TaskCompletionSource<byte[]> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingRequests[requestId] = tcs;

        try
        {
            await SendRaw(connection, new TcpMessage
            {
                Type = type,
                RequestId = requestId,
                Payload = JsonSerializer.SerializeToUtf8Bytes(request)
            });

            // Enforce async timeout safety to prevent permanent dictionary leaks on dropped calls
            using CancellationTokenSource timeoutCts = new(timeout);
            await using (timeoutCts.Token.Register(() => tcs.TrySetCanceled()))
            {
                byte[] responseBytes = await tcs.Task;
                return JsonSerializer.Deserialize<TRes>(responseBytes)!;
            }
        }
        finally
        {
            _ = pendingRequests.TryRemove(requestId, out _);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Handled via library constraints")]
    public Task SendAsync<T>(string type, T data)
    {
        if (connection is null)
        {
            throw new InvalidOperationException("Client not connected.");
        }

        return SendRaw(connection, new TcpMessage
        {
            Type = type,
            RequestId = Guid.NewGuid().ToString(),
            Payload = JsonSerializer.SerializeToUtf8Bytes(data)
        });
    }

    protected override void OnConnectionClosed(TcpConnection conn)
    {
        if (connection == conn)
        {
            connection = null;
        }

        // Fail-fast all lingering tasks waiting on an dead connection loop
        foreach (TaskCompletionSource<byte[]> req in pendingRequests.Values)
        {
            _ = req.TrySetException(new SocketException((int)SocketError.ConnectionReset));
        }
        pendingRequests.Clear();
    }
}