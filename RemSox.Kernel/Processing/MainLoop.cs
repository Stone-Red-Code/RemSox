using RemSox.Kernel.Logging;

namespace RemSox.Kernel.Processing;

/// <summary>
/// The kernel's main loop as a <see cref="SynchronizationContext"/>: the one thread that owns the
/// system's state (processes, windows, shells, settings). Other threads only do blocking I/O and
/// hand their results over with <see cref="Post(Action)"/>.
/// </summary>
/// <remarks>
/// It is installed on the main thread at boot, so code that awaits there (every command, every
/// shell) also resumes there, whichever thread completes the awaited task. Posted work runs at
/// the start of the next loop iteration, before processes tick.
/// </remarks>
public sealed class MainLoop : SynchronizationContext
{
    private static readonly MainLoop instance = new();
    private static readonly Lock pendingLock = new();
    private static Queue<(SendOrPostCallback Callback, object? State)> pending = new();
    private static Queue<(SendOrPostCallback Callback, object? State)> running = new();
    private static int mainThreadId = -1;

    private MainLoop()
    {
    }

    /// <summary>Gets whether the caller runs on the main loop's thread.</summary>
    public static bool IsMainThread => Environment.CurrentManagedThreadId == mainThreadId;

    /// <summary>
    /// Reports on the serial port when called off the main loop: state is only safe to touch
    /// there, so a call from another thread is a bug that would otherwise show as a rare race.
    /// </summary>
    public static void AssertMainThread(string caller)
    {
        if (mainThreadId != -1 && !IsMainThread)
        {
            Cosmos.Kernel.System.Diagnostics.Log.WriteString($"[RemSox] {caller} called off the main loop (thread {Environment.CurrentManagedThreadId})" + Environment.NewLine);
        }
    }

    /// <summary>Makes the calling thread the main loop and installs the context on it. Called once, at boot.</summary>
    internal static void Install()
    {
        mainThreadId = Environment.CurrentManagedThreadId;
        SetSynchronizationContext(instance);
    }

    /// <summary>Runs <paramref name="action"/> on the main loop, from any thread.</summary>
    public static void Post(Action action)
    {
        instance.Post(static state => ((Action)state!)(), action);
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (pendingLock)
        {
            pending.Enqueue((d, state));
        }
    }

    /// <summary>Runs <paramref name="d"/> at once on the main loop; another thread would have to block, which is not allowed.</summary>
    public override void Send(SendOrPostCallback d, object? state)
    {
        if (!IsMainThread)
        {
            throw new NotSupportedException("Only the main loop may run work synchronously; post it instead.");
        }

        d(state);
    }

    public override SynchronizationContext CreateCopy()
    {
        return this;
    }

    /// <summary>
    /// Runs the work posted so far. Work posted while it runs waits for the next iteration, so a
    /// callback that posts again cannot keep the loop here.
    /// </summary>
    internal static void RunPending()
    {
        lock (pendingLock)
        {
            (pending, running) = (running, pending);
        }

        while (running.TryDequeue(out (SendOrPostCallback Callback, object? State) work))
        {
            try
            {
                work.Callback(work.State);
            }
            catch (Exception ex)
            {
                SystemLog.Log("Work posted to the main loop failed: " + ex, LogSeverity.Error);
            }
        }
    }
}
