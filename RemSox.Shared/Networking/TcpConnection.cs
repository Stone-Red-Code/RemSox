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
    /// Encrypts this connection's messages once its login (<see cref="RpcHandshake"/>) has
    /// succeeded; null until then.
    /// </summary>
    public IPacketCrypto? Crypto { get; set; }

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
