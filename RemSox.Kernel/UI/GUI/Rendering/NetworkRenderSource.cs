using RemSox.Shared.Networking;
using RemSox.Shared.UI.GUI.Rendering;

namespace RemSox.Kernel.UI.GUI.Rendering;

/// <summary>
/// Forwards render commands to remote viewers. Commands wait here until the server has room,
/// then go out in batches (<see cref="Flush"/>, called by the remote desktop process each tick),
/// so a slow viewer delays only itself and the cursor is sent at its latest position.
/// </summary>
public sealed class NetworkRenderSource(TcpRpcServer server) : IRenderSource
{
    // Cosmos' TCP send is stop-and-wait in 536-byte segments, so each send costs a round trip per
    // segment. Batches are kept to a single segment: one round trip carries as many commands as fit.
    private const int MaxSegmentSize = 536;

    // Batches handed to the server but not written yet. Few, so commands wait here instead, where
    // cursor moves still collapse into one.
    private const int MaxQueuedBatches = 4;

    private static readonly int maxBatchPayload = MaxSegmentSize - TcpRpcServer.GetPushOverhead(RemoteInputProtocol.RenderBatch);

    private readonly Queue<byte[]> queue = new();

    // The latest cursor position not sent yet: only the newest one matters.
    private byte[]? cursor;

    public void Render(IEnumerable<RenderCommand> commands)
    {
        if (!server.HasConnections)
        {
            return;
        }

        foreach (RenderCommand command in commands)
        {
            if (command.Type == RenderCommandType.SetCursor)
            {
                cursor = command.ToBytes();
            }
            else
            {
                queue.Enqueue(command.ToBytes());
            }
        }
    }

    public void Composite()
    {
    }

    /// <summary>Hands waiting commands to the server in batches, as long as it has room for them.</summary>
    public void Flush()
    {
        if (!server.HasConnections)
        {
            queue.Clear();
            cursor = null;
            return;
        }

        while ((queue.Count > 0 || cursor is not null) && server.MaxQueuedPackets < MaxQueuedBatches)
        {
            // Take commands in order until the batch would no longer fit in one segment.
            List<byte[]> batch = [];
            int size = 0;
            while (queue.TryPeek(out byte[]? data) && (batch.Count == 0 || size + 4 + data.Length <= maxBatchPayload))
            {
                batch.Add(queue.Dequeue());
                size += 4 + data.Length;
            }

            if (cursor is not null && (batch.Count == 0 || size + 4 + cursor.Length <= maxBatchPayload))
            {
                batch.Add(cursor);
                cursor = null;
            }

            server.SendRawToAll(RemoteInputProtocol.RenderBatch, RemoteInputProtocol.SerializeRenderBatch(batch));
        }
    }
}
