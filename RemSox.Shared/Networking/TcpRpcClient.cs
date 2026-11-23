using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Authentication;

namespace RemSox.Shared.Networking;

/// <summary>
/// The client end of an RPC connection: logs in with the server's password, then receives on a
/// thread of its own and sends from any thread.
/// </summary>
public sealed class TcpRpcClient : TcpRpcBase
{
    private TcpConnection? connection;

    /// <summary>Raised on the receive thread when the connection to the server is lost.</summary>
    public event Action? Disconnected;

    public bool IsConnected => connection is not null;

    /// <summary>
    /// Connects and logs in as <paramref name="user"/> (<see cref="RpcHandshake"/>), blocking
    /// until the server has accepted the login; then starts receiving. Includes the slow password
    /// derivation, so call it off the UI thread.
    /// </summary>
    /// <exception cref="AuthenticationException">
    /// The name or password is wrong, the server refused the login (the message says why), or
    /// it could not prove it knows the password.
    /// </exception>
    /// <exception cref="IOException">
    /// The server did not answer in time, closed the connection, or does not speak this protocol.
    /// </exception>
    /// <exception cref="SocketException">The connection could not be made.</exception>
    public void Connect(string host, int port, string user, string password)
    {
        TcpClient client = new();
        TcpConnection newConnection;

        try
        {
            client.Connect(host, port);
            newConnection = new TcpConnection(client);
            LogIn(newConnection, user, password);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        connection = newConnection;
        _ = Task.Run(() => HandleConnection(newConnection));
    }

    /// <summary>
    /// Closes the connection. The receive loop then ends and raises <see cref="Disconnected"/>.
    /// </summary>
    public void Disconnect()
    {
        connection?.Dispose();
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

        Disconnected?.Invoke();
    }

    /// <summary>Runs the client's side of the handshake and switches the connection to encryption.</summary>
    private static void LogIn(TcpConnection conn, string user, string password)
    {
        int timeoutMs = (int)RpcHandshake.Timeout.TotalMilliseconds;
        conn.Client.ReceiveTimeout = timeoutMs;
        conn.Client.SendTimeout = timeoutMs;

        try
        {
            byte[] hello = Frame(RpcHandshake.CreateHello(user));
            conn.Stream.Write(hello, 0, hello.Length);

            (byte[] answer, RpcHandshake.SessionKeys keys) = RpcHandshake.AnswerChallenge(user, password, ReadHandshakePacket(conn));

            byte[] login = Frame(answer);
            conn.Stream.Write(login, 0, login.Length);

            RpcHandshake.CheckResult(ReadHandshakePacket(conn), keys);
            conn.Crypto = new AesGcmSession(keys.ClientToServer, keys.ServerToClient);
        }
        catch (InvalidDataException ex)
        {
            throw new IOException(ex.Message, ex);
        }

        conn.Client.ReceiveTimeout = 0;
        conn.Client.SendTimeout = 0;
    }

    /// <summary>
    /// Reads one unencrypted handshake packet, exactly: whatever the server sends after it
    /// (encrypted) stays in the socket for the receive loop.
    /// </summary>
    private static byte[] ReadHandshakePacket(TcpConnection conn)
    {
        byte[] lengthBytes = ReadExactly(conn, 4);
        int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length is <= 0 or > RpcHandshake.MaxPacketSize)
        {
            throw new InvalidDataException("The server does not speak the RemSox RPC protocol.");
        }

        return ReadExactly(conn, length);
    }

    private static byte[] ReadExactly(TcpConnection conn, int count)
    {
        byte[] buffer = new byte[count];
        int offset = 0;

        while (offset < count)
        {
            int read = conn.Stream.Read(buffer, offset, count - offset);
            if (read == 0)
            {
                throw new IOException("The server closed the connection.");
            }

            offset += read;
        }

        return buffer;
    }
}
