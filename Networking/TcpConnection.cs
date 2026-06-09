using System.Net.Sockets;

/// <summary>
/// Simple wrapper around TcpClient to hold connection-scoped resources safely.
/// </summary>
public class TcpConnection(TcpClient client) : IDisposable
{
    public TcpClient Client { get; } = client;
    public NetworkStream Stream { get; } = client.GetStream();
    public SemaphoreSlim SendLock { get; } = new(1, 1);

    public void Dispose()
    {
        Stream.Dispose();
        Client.Dispose();
        SendLock.Dispose();
    }
}