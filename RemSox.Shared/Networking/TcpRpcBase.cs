using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;

namespace RemSox.Shared.Networking;

/// <summary>
/// Message framing and dispatch shared by the RPC client and server. A packet is a little-endian
/// int32 length followed by the (optionally encrypted) message: its type, request id and payload,
/// each prefixed with its int32 length.
/// </summary>
public abstract class TcpRpcBase(IPacketCrypto? crypto = null)
{
    protected readonly IPacketCrypto? crypto = crypto;
    protected readonly Dictionary<string, Func<TcpConnection, TcpMessage, Task>> handlers = [];
    protected readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> pendingRequests = new();

    // Security Guard: Prevent OOM/DoS via oversized length headers (Default: 32MB)
    protected const int MaxMessageSize = 32 * 1024 * 1024;

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

    /// <summary>Writes <paramref name="msg"/> to <paramref name="conn"/> at once, blocking until it is sent.</summary>
    protected virtual void SendRaw(TcpConnection conn, TcpMessage msg)
    {
        byte[] packet = BuildPacket(msg);

        lock (conn.SendLock)
        {
            conn.Stream.Write(packet, 0, packet.Length);
        }
    }

    /// <summary>Serializes, encrypts and length-prefixes <paramref name="msg"/>, ready to write.</summary>
    protected byte[] BuildPacket(TcpMessage msg)
    {
        byte[] rawData = SerializeMessage(msg);

        if (crypto is not null)
        {
            rawData = crypto.Encrypt(rawData);
        }

        byte[] packet = new byte[4 + rawData.Length];
        BinaryPrimitives.WriteInt32LittleEndian(packet, rawData.Length);
        Array.Copy(rawData, 0, packet, 4, rawData.Length);
        return packet;
    }

    /// <summary>
    /// Handles one received packet body (without its length prefix): completes the request it
    /// answers, or runs the handler for its type. Handlers run one at a time in arrival order:
    /// render commands depend on the windows created before them, and input events on the ones
    /// sent before them.
    /// </summary>
    protected void Dispatch(TcpConnection conn, byte[] body)
    {
        if (crypto is not null)
        {
            body = crypto.Decrypt(body);
        }

        TcpMessage? msg = DeserializeMessage(body);
        if (msg is null)
        {
            Log("[TcpRpc] Failed to deserialize incoming message.");
            return;
        }

        if (pendingRequests.TryRemove(msg.RequestId, out TaskCompletionSource<byte[]>? tcs))
        {
            _ = tcs.TrySetResult(msg.Payload);
            return;
        }

        if (handlers.TryGetValue(msg.Type, out Func<TcpConnection, TcpMessage, Task>? handler))
        {
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

    // Size of the per-connection receive buffer. Each socket read asks for this much, so everything
    // the stack has buffered is taken in one call.
    private const int ReceiveBufferSize = 64 * 1024;

    /// <summary>
    /// Copies exactly <paramref name="count"/> bytes into <paramref name="buffer"/>, refilling the
    /// connection's receive buffer from the socket as needed.
    /// </summary>
    /// <returns>False once the peer has closed the connection.</returns>
    private static bool ReadExact(TcpConnection conn, ReceiveBuffer received, byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            if (received.Available == 0)
            {
                // Read waits for data and returns 0 only once the peer has closed the connection.
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

    /// <summary>
    /// Reads and dispatches packets until the connection closes, blocking the calling thread (the
    /// client's receive thread). A packet with an impossible length ends the connection, since the
    /// stream can no longer be followed.
    /// </summary>
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
                    break;
                }

                byte[] buffer = new byte[length];
                if (!ReadExact(conn, received, buffer, 0, length))
                {
                    break;
                }

                Dispatch(conn, buffer);
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
