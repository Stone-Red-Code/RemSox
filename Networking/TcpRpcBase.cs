using RemSox.Networking;

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Text.Json;

public abstract class TcpRpcBase(IPacketCrypto? crypto = null)
{
    protected readonly IPacketCrypto? crypto = crypto;
    protected readonly Dictionary<string, Func<TcpConnection, TcpMessage, Task>> handlers = [];
    protected readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> pendingRequests = new();

    // Security Guard: Prevent OOM/DoS via oversized length headers (Default: 32MB)
    protected const int MaxMessageSize = 32 * 1024 * 1024;

    /// <summary>
    /// Base message registration for handlers tracking the sending connection.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Handled via library constraints")]
    public void ListenTo<T>(string type, Func<TcpConnection, T, Task> handler)
    {
        handlers[type] = async (conn, msg) =>
        {
            T data = JsonSerializer.Deserialize<T>(msg.Payload)!;
            await handler(conn, data);
        };
    }

    /// <summary>
    /// Overloaded listener where connection mapping can be ignored (Convenient for Clients).
    /// </summary>
    public void ListenTo<T>(string type, Func<T, Task> handler)
    {
        ListenTo<T>(type, async (_, data) => await handler(data));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Handled via library constraints")]
    protected async Task SendRaw(TcpConnection conn, TcpMessage msg)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(msg);

        if (crypto is not null)
        {
            data = crypto.Encrypt(data);
        }

        // Allocate a single contiguous frame buffer to prevent inter-thread fragmentation 
        // and eliminate redundant Socket Write Syscalls.
        byte[] packet = new byte[4 + data.Length];
        BinaryPrimitives.WriteInt32LittleEndian(packet, data.Length);
        Array.Copy(data, 0, packet, 4, data.Length);

        // Enforce sequence safety across multiple threads pushing data out of a singular stream
        await conn.SendLock.WaitAsync();
        try
        {
            await conn.Stream.WriteAsync(packet);
        }
        finally
        {
            _ = conn.SendLock.Release();
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Handled via library constraints")]
    protected async Task HandleConnection(TcpConnection conn, CancellationToken token = default)
    {
        NetworkStream stream = conn.Stream;
        byte[] lengthBytes = new byte[4];

        try
        {
            while (!token.IsCancellationRequested && conn.Client.Connected)
            {
                // 1. Frame Length Read
                await stream.ReadExactlyAsync(lengthBytes, token);
                int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);

                // DoS Payload Protection Check
                if (length is <= 0 or > MaxMessageSize)
                {
                    throw new InvalidDataException($"Protocol violation: Received packet length of {length} bytes exceeds limits.");
                }

                // 2. Body Payload Read
                byte[] buffer = new byte[length];
                await stream.ReadExactlyAsync(buffer, token);

                if (crypto is not null)
                {
                    buffer = crypto.Decrypt(buffer);
                }

                TcpMessage? msg = JsonSerializer.Deserialize<TcpMessage>(buffer);
                if (msg is null)
                {
                    continue;
                }

                // 3. Response Handler Check
                if (pendingRequests.TryGetValue(msg.RequestId, out TaskCompletionSource<byte[]>? tcs))
                {
                    _ = tcs.TrySetResult(msg.Payload);
                    _ = pendingRequests.TryRemove(msg.RequestId, out _);
                    continue;
                }

                // 4. Inbound Router Handler
                if (handlers.TryGetValue(msg.Type, out Func<TcpConnection, TcpMessage, Task>? handler))
                {
                    // Decouple handler execution to the ThreadPool so slow business logic 
                    // doesn't bottleneck packet processing from the network stream interface.
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await handler(conn, msg);
                        }
                        catch
                        {
                            // Operational log placement here for user exceptions inside delegates
                        }
                    }, token);
                }
            }
        }
        catch
        {
            // Explicitly handles natural dropping scenarios cleanly
        }
        finally
        {
            OnConnectionClosed(conn);
            conn.Dispose();
        }
    }

    protected abstract void OnConnectionClosed(TcpConnection conn);
}