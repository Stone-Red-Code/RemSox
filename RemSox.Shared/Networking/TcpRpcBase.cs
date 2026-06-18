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

        lock (conn.SendLock)
        {
            conn.Stream.Write(packet, 0, packet.Length);
        }
    }

    private static void ReadExact(NetworkStream stream, byte[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = stream.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
            {
                Thread.Sleep(10);
                continue;
            }
            totalRead += read;
        }
    }

    protected void HandleConnection(TcpConnection conn, CancellationToken token = default)
    {
        NetworkStream stream = conn.Stream;
        byte[] lengthBytes = new byte[4];

        try
        {
            while (!token.IsCancellationRequested && conn.Client.Connected)
            {
                Console.WriteLine("[TcpRpc] Waiting for incoming packet...");

                ReadExact(stream, lengthBytes, 0, 4);
                int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);

                if (length is <= 0 or > MaxMessageSize)
                {
                    Console.WriteLine($"[TcpRpc] Invalid packet length: {length}");
                    Thread.Sleep(10);
                    continue;
                }

                Console.WriteLine($"[TcpRpc] Incoming packet length: {length} bytes");

                byte[] buffer = new byte[length];
                ReadExact(stream, buffer, 0, length);

                Console.WriteLine($"[TcpRpc] Incoming packet payload: {BitConverter.ToString(buffer)}");

                if (crypto is not null)
                {
                    buffer = crypto.Decrypt(buffer);
                }

                Console.WriteLine($"[TcpRpc] Decrypted packet payload: {BitConverter.ToString(buffer)}");

                TcpMessage? msg = DeserializeMessage(buffer);
                if (msg is null)
                {
                    Console.WriteLine("[TcpRpc] Failed to deserialize incoming message.");
                    continue;
                }

                Console.WriteLine($"[TcpRpc] Incoming message type: {msg.Type}, requestId: {msg.RequestId}, payload length: {msg.Payload.Length} bytes");

                if (pendingRequests.TryGetValue(msg.RequestId, out TaskCompletionSource<byte[]>? tcs))
                {
                    _ = tcs.TrySetResult(msg.Payload);
                    _ = pendingRequests.TryRemove(msg.RequestId, out _);
                    continue;
                }

                if (handlers.TryGetValue(msg.Type, out Func<TcpConnection, TcpMessage, Task>? handler))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await handler(conn, msg);
                        }
                        catch
                        {
                        }
                    }, token);
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
