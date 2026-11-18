namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// Keys handed from a shell's input loop to a full-screen program, buffered so none is lost while
/// the program is busy redrawing.
/// </summary>
public sealed class KeyQueue
{
    private readonly Queue<ConsoleKeyInfo> keys = new();
    private readonly Lock queueLock = new();
    private TaskCompletionSource<ConsoleKeyInfo?>? waiting;

    public void Enqueue(ConsoleKeyInfo key)
    {
        TaskCompletionSource<ConsoleKeyInfo?>? reader;

        lock (queueLock)
        {
            reader = waiting;
            waiting = null;

            if (reader is null)
            {
                keys.Enqueue(key);
                return;
            }
        }

        _ = reader.TrySetResult(key);
    }

    public async Task<ConsoleKeyInfo?> ReadAsync(CancellationToken cancellation)
    {
        TaskCompletionSource<ConsoleKeyInfo?> reader;

        lock (queueLock)
        {
            if (keys.TryDequeue(out ConsoleKeyInfo key))
            {
                return key;
            }

            reader = new TaskCompletionSource<ConsoleKeyInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
            waiting = reader;
        }

        using (cancellation.Register(() => reader.TrySetResult(null)))
        {
            return await reader.Task;
        }
    }

    /// <summary>Drops buffered keys and wakes a waiting reader with null.</summary>
    public void Close()
    {
        TaskCompletionSource<ConsoleKeyInfo?>? reader;

        lock (queueLock)
        {
            keys.Clear();
            reader = waiting;
            waiting = null;
        }

        _ = reader?.TrySetResult(null);
    }
}
