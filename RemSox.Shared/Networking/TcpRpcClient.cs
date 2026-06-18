using System.Net;
using System.Net.Sockets;

namespace RemSox.Shared.Networking;

public class TcpRpcClient(IPacketCrypto? crypto = null) : TcpRpcBase(crypto)
{
    private TcpConnection? connection;

    public void Connect(string host, int port)
    {
        TcpClient client = new();
        client.Connect(host, port);

        connection = new TcpConnection(client);

        _ = Task.Run(() => HandleConnection(connection));
    }

    public async Task<byte[]> RequestAsync(string type, byte[] request, TimeSpan timeout = default)
    {
        if (connection is null)
        {
            throw new InvalidOperationException("Client not connected.");
        }

        if (timeout == default)
        {
            timeout = TimeSpan.FromSeconds(30);
        }

        string requestId = Guid.NewGuid().ToString();
        TaskCompletionSource<byte[]> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingRequests[requestId] = tcs;

        try
        {
            SendRaw(connection, new TcpMessage
            {
                Type = type,
                RequestId = requestId,
                Payload = request
            });

            using CancellationTokenSource timeoutCts = new(timeout);
            using (timeoutCts.Token.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            _ = pendingRequests.TryRemove(requestId, out _);
        }
    }

    public void Send(string type, byte[] data)
    {
        if (connection is null)
        {
            throw new InvalidOperationException("Client not connected.");
        }

        SendRaw(connection, new TcpMessage
        {
            Type = type,
            RequestId = Guid.NewGuid().ToString(),
            Payload = data
        });
    }

    protected override void OnConnectionClosed(TcpConnection conn)
    {
        if (connection == conn)
        {
            connection = null;
        }

        foreach (TaskCompletionSource<byte[]> req in pendingRequests.Values)
        {
            _ = req.TrySetException(new SocketException((int)SocketError.ConnectionReset));
        }
        pendingRequests.Clear();
    }
}
