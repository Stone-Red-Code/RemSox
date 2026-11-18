using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace RemSox.Shared.Networking;

public abstract class TcpRpcBase(IPacketCrypto? crypto = null)
{
    protected readonly IPacketCrypto? crypto = crypto;
    protected readonly Dictionary<string, Func<TcpConnection, TcpMessage, Task>> handlers = [];
    protected readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> pendingRequests = new();

    // Security Guard: Prevent OOM/DoS via oversized length headers (Default: 32MB)
    protected const int MaxMessageSize = 32 * 1024 * 1024;

    // With SerializeSocketIo, how many packets may wait for a connection's I/O thread before
    // senders block (the back-pressure a blocking write would otherwise give), and how long that
    // thread waits for incoming data before it checks for packets to send again.
    private const int MaxQueuedPackets = 64;
    private const int PollMicroseconds = 2_000;

    /// <summary>
    /// Whether each connection's socket is used by its I/O thread alone. Sends are then queued
    /// and written by that thread between waits for incoming data, instead of being written by the
    /// sending thread while the I/O thread reads.
    /// </summary>
    /// <remarks>
    /// Needed on Cosmos, whose sockets are not safe to use from two threads at once. Standard .NET
    /// sockets are, and leave this off for the lower latency of blocking reads and direct writes.
    /// </remarks>
    public bool SerializeSocketIo { get; init; }

    /// <summary>Receives diagnostic messages (connections, malformed packets, handler failures).</summary>
    public Action<string> Log { get; init; } = Console.WriteLine;

    public void ListenTo(string type, Func<TcpConnection, byte[], Task> handler)
    {
        handlers[type] = async (conn, msg) => await handler(conn, msg.Payload);
    }

    public void ListenTo(string type, Func<byte[], Task> handler)
    {
        handlers[type] = async (conn, msg) => await handler(msg.Payload);
    }

    public void RespondTo(string type, Func<byte[], Task<byte[]>> handler)
    {
        handlers[type] = async (conn, msg) =>
        {
            byte[] res = await handler(msg.Payload);
            SendRaw(conn, new TcpMessage
            {
                Type = type,
                RequestId = msg.RequestId,
                Payload = res
            });
        };
    }

    protected void SendRaw(TcpConnection conn, TcpMessage msg)
    {
        byte[] rawData = SerializeMessage(msg);

        if (crypto is not null)
        {
            rawData = crypto.Encrypt(rawData);
        }

        byte[] packet = new byte[4 + rawData.Length];
        BinaryPrimitives.WriteInt32LittleEndian(packet, rawData.Length);
        Array.Copy(rawData, 0, packet, 4, rawData.Length);

        if (SerializeSocketIo)
        {
            while (conn.Outgoing.Count >= MaxQueuedPackets && !conn.IsClosed)
            {
                Thread.Sleep(1);
            }

            if (!conn.IsClosed)
            {
                conn.Outgoing.Enqueue(packet);
            }

            return;
        }

        lock (conn.SendLock)
        {
            conn.Stream.Write(packet, 0, packet.Length);
        }
    }

    /// <summary>
    /// Writes the packets queued for <paramref name="conn"/>. Called only by its I/O thread.
    /// </summary>
    private static void FlushOutgoing(TcpConnection conn)
    {
        while (conn.Outgoing.TryDequeue(out byte[]? packet))
        {
            conn.Stream.Write(packet, 0, packet.Length);
        }
    }

    // Size of the per-connection receive buffer. Each socket read asks for this much, so everything
    // the stack has buffered is taken in one call.
    private const int ReceiveBufferSize = 64 * 1024;

    /// <summary>
    /// Copies exactly <paramref name="count"/> bytes into <paramref name="buffer"/>, refilling the
    /// connection's receive buffer from the socket as needed.
    /// </summary>
    /// <returns>False once the peer has closed the connection.</returns>
    /// <remarks>
    /// A close is reported by the return value rather than an exception: it is the normal end of
    /// every connection. On Cosmos 3.0.97, throwing it on the server's per-client threads faulted
    /// the CPU inside the runtime's exception dispatch (a null read in its stack trace lookup).
    /// </remarks>
    private bool ReadExact(TcpConnection conn, ReceiveBuffer received, byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            if (received.Available == 0)
            {
                if (SerializeSocketIo)
                {
                    // Send what is queued while waiting for data. Poll reports a closed connection
                    // as readable, and the Read below then returns 0.
                    do
                    {
                        if (conn.IsClosed)
                        {
                            return false;
                        }

                        FlushOutgoing(conn);
                    }
                    while (!conn.Client.Client.Poll(PollMicroseconds, SelectMode.SelectRead));
                }

                // Read waits for data and returns 0 only once the peer has closed the connection,
                // on .NET and on Cosmos (since 3.0.90).
                int read = conn.Stream.Read(received.Data, 0, received.Data.Length);
                if (read == 0)
                {
                    return false;
                }

                received.Start = 0;
                received.End = read;
            }

            int chunk = Math.Min(count, received.Available);
            Array.Copy(received.Data, received.Start, buffer, offset, chunk);
            received.Start += chunk;
            offset += chunk;
            count -= chunk;
        }

        return true;
    }

    private sealed class ReceiveBuffer
    {
        public byte[] Data { get; } = new byte[ReceiveBufferSize];

        public int Start { get; set; }

        public int End { get; set; }

        public int Available => End - Start;
    }

    protected void HandleConnection(TcpConnection conn, CancellationToken token = default)
    {
        byte[] lengthBytes = new byte[4];
        ReceiveBuffer received = new();

        try
        {
            while (!token.IsCancellationRequested && conn.Client.Connected)
            {
                if (!ReadExact(conn, received, lengthBytes, 0, 4))
                {
                    break;
                }

                int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);

                if (length is <= 0 or > MaxMessageSize)
                {
                    Log($"[TcpRpc] Invalid packet length: {length}");
                    Thread.Sleep(10);
                    continue;
                }

                byte[] buffer = new byte[length];
                if (!ReadExact(conn, received, buffer, 0, length))
                {
                    break;
                }

                if (crypto is not null)
                {
                    buffer = crypto.Decrypt(buffer);
                }

                TcpMessage? msg = DeserializeMessage(buffer);
                if (msg is null)
                {
                    Log("[TcpRpc] Failed to deserialize incoming message.");
                    continue;
                }

                if (pendingRequests.TryGetValue(msg.RequestId, out TaskCompletionSource<byte[]>? tcs))
                {
                    _ = tcs.TrySetResult(msg.Payload);
                    _ = pendingRequests.TryRemove(msg.RequestId, out _);
                    continue;
                }

                if (handlers.TryGetValue(msg.Type, out Func<TcpConnection, TcpMessage, Task>? handler))
                {
                    // Run handlers one at a time in arrival order: render commands depend on the
                    // windows created before them, and input events on the ones sent before them.
                    try
                    {
                        handler(conn, msg).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        Log($"[TcpRpc] Handler for {msg.Type} failed: {ex.Message}");
                    }
                }
            }
        }
        catch
        {
        }
        finally
        {
            OnConnectionClosed(conn);
            conn.Dispose();
        }
    }

    private static byte[] SerializeMessage(TcpMessage msg)
    {
        byte[] typeBytes = Encoding.UTF8.GetBytes(msg.Type);
        byte[] requestIdBytes = Encoding.UTF8.GetBytes(msg.RequestId);

        byte[] data = new byte[4 + typeBytes.Length + 4 + requestIdBytes.Length + 4 + msg.Payload.Length];
        int offset = 0;

        WriteInt32(data, ref offset, typeBytes.Length);
        typeBytes.CopyTo(data, offset);
        offset += typeBytes.Length;

        WriteInt32(data, ref offset, requestIdBytes.Length);
        requestIdBytes.CopyTo(data, offset);
        offset += requestIdBytes.Length;

        WriteInt32(data, ref offset, msg.Payload.Length);
        msg.Payload.CopyTo(data, offset);

        return data;
    }

    private static TcpMessage? DeserializeMessage(byte[] data)
    {
        int offset = 0;
        if (offset + 4 > data.Length)
        {
            return null;
        }

        int typeLen = ReadInt32(data, ref offset);
        if (offset + typeLen > data.Length)
        {
            return null;
        }

        string type = Encoding.UTF8.GetString(data, offset, typeLen);
        offset += typeLen;

        if (offset + 4 > data.Length)
        {
            return null;
        }

        int requestIdLen = ReadInt32(data, ref offset);
        if (offset + requestIdLen > data.Length)
        {
            return null;
        }

        string requestId = Encoding.UTF8.GetString(data, offset, requestIdLen);
        offset += requestIdLen;

        if (offset + 4 > data.Length)
        {
            return null;
        }

        int payloadLen = ReadInt32(data, ref offset);
        if (offset + payloadLen > data.Length)
        {
            return null;
        }

        byte[] payload = new byte[payloadLen];
        Array.Copy(data, offset, payload, 0, payloadLen);

        return new TcpMessage { Type = type, RequestId = requestId, Payload = payload };
    }

    private static void WriteInt32(byte[] data, ref int offset, int value)
    {
        data[offset++] = (byte)(value & 0xFF);
        data[offset++] = (byte)((value >> 8) & 0xFF);
        data[offset++] = (byte)((value >> 16) & 0xFF);
        data[offset++] = (byte)((value >> 24) & 0xFF);
    }

    private static int ReadInt32(byte[] data, ref int offset)
    {
        int val = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
        offset += 4;
        return val;
    }

    protected abstract void OnConnectionClosed(TcpConnection conn);
}
