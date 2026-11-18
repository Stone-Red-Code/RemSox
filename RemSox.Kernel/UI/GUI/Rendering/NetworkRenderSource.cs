using RemSox.Shared.Networking;
using RemSox.Shared.UI.GUI.Rendering;

namespace RemSox.Kernel.UI.GUI.Rendering;

/// <summary>
/// Forwards render commands to remote viewers. Commands are queued and sent in batches from a
/// background thread, so the local renderer never waits on the network.
/// </summary>
public sealed class NetworkRenderSource : IRenderSource, IDisposable
{
    // Cosmos' TCP send is stop-and-wait in 536-byte segments, so each send costs a round trip per
    // segment. Batches are kept to a single segment: one round trip carries as many commands as fit.
    private const int MaxSegmentSize = 536;

    private static readonly int maxBatchPayload = MaxSegmentSize - TcpRpcServer.GetPushOverhead(RemoteInputProtocol.RenderBatch);

    private readonly TcpRpcServer server;
    private readonly object queueLock = new();
    private readonly List<(RenderCommandType Type, byte[] Data)> queue = [];
    private readonly Thread sender;
    private volatile bool running = true;

    public NetworkRenderSource(TcpRpcServer server)
    {
        this.server = server;
        sender = new Thread(SendLoop) { IsBackground = true, Name = "RDP render sender" };
        sender.Start();
    }

    public void Render(IEnumerable<RenderCommand> commands)
    {
        if (!server.HasConnections)
        {
            return;
        }

        lock (queueLock)
        {
            foreach (RenderCommand command in commands)
            {
                if (command.Type == RenderCommandType.SetCursor)
                {
                    // Only the latest cursor position matters if the viewer has fallen behind.
                    _ = queue.RemoveAll(c => c.Type == RenderCommandType.SetCursor);
                }

                queue.Add((command.Type, command.ToBytes()));
            }

            Monitor.Pulse(queueLock);
        }
    }

    public void Composite()
    {
    }

    public void Dispose()
    {
        running = false;

        lock (queueLock)
        {
            queue.Clear();
            Monitor.Pulse(queueLock);
        }
    }

    private void SendLoop()
    {
        List<byte[]> batch = [];

        while (running)
        {
            batch.Clear();

            lock (queueLock)
            {
                while (running && queue.Count == 0)
                {
                    _ = Monitor.Wait(queueLock, 100);
                }

                // Take commands in order until the batch would no longer fit in one segment.
                int size = 0;
                int taken = 0;
                foreach ((RenderCommandType _, byte[] data) in queue)
                {
                    int entrySize = 4 + data.Length;
                    if (taken > 0 && size + entrySize > maxBatchPayload)
                    {
                        break;
                    }

                    batch.Add(data);
                    size += entrySize;
                    taken++;
                }

                queue.RemoveRange(0, taken);
            }

            if (batch.Count == 0)
            {
                continue;
            }

            try
            {
                server.SendRawToAll(RemoteInputProtocol.RenderBatch, RemoteInputProtocol.SerializeRenderBatch(batch));
            }
            catch (Exception)
            {
                // The connection went away (Cosmos' TCP send throws a plain Exception then); the
                // server's receive loop cleans it up. Nothing may escape: an unhandled exception on
                // this thread would halt the kernel.
            }
        }
    }
}
