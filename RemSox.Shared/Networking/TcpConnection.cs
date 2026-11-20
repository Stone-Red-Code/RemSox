using System.Collections.Concurrent;
using System.Net.Sockets;

namespace RemSox.Shared.Networking;

/// <summary>
/// Simple wrapper around TcpClient to hold connection-scoped resources safely.
/// </summary>
public sealed class TcpConnection(TcpClient client) : IDisposable
{
    public TcpClient Client { get; } = client;
    public NetworkStream Stream { get; } = client.GetStream();
    public object SendLock { get; } = new();

    /// <summary>
    /// Packets waiting to be written by the connection's I/O thread, when
    /// <see cref="TcpRpcBase.SerializeSocketIo"/> is on.
    /// </summary>
    internal ConcurrentQueue<byte[]> Outgoing { get; } = new();

    /// <summary>True once the connection has been disposed.</summary>
    public bool IsClosed => closed;

    private volatile bool closed;

    public void Dispose()
    {
        closed = true;
        Stream.Dispose();
        Client.Dispose();
    }
}
