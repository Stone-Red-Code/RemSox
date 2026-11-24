using System.Diagnostics;

using RemSox.Kernel.Logging;

namespace RemSox.Kernel.Processing;

/// <summary>
/// The kernel's main loop as a <see cref="SynchronizationContext"/>: the one thread that owns the
/// system's state (processes, windows, shells, settings). Other threads only do blocking I/O and
/// hand their results over with <see cref="Post(Action)"/>.
/// </summary>
/// <remarks>
/// It is installed on the main thread at boot, so code that awaits there (every command, every
/// shell) also resumes there, whichever thread completes the awaited task. Each iteration
/// (<see cref="RunOnce"/>) first runs the work posted since the last one, then ticks every
/// process: posted work is what arrives, ticks are what processes poll for themselves.
/// Between iterations it idles (<see cref="Idle"/>), so the processor halts when there is
/// nothing to do and its usage means something.
/// </remarks>
public sealed class MainLoop : SynchronizationContext
{
    private static readonly MainLoop instance = new();
    private static readonly Lock pendingLock = new();
    private static Queue<(SendOrPostCallback Callback, object? State)> pending = new();
    private static Queue<(SendOrPostCallback Callback, object? State)> running = new();
    private static int mainThreadId = -1;

    /// <summary>
    /// How long an iteration lasts at least when there is nothing to do, in milliseconds: the
    /// loop then sleeps for the rest, about 100 iterations a second. Input, network polling and
    /// drawing wait at most this long; the processor halts meanwhile.
    /// </summary>
    private const int IdleIterationMilliseconds = 10;

    // When the current iteration began (a Stopwatch timestamp), and whether something asked
    // for the next one at once.
    private static long iterationStart;
    private static bool stayAwake;

    /// <summary>
    /// Gets how long the loop slept since boot, in <see cref="Stopwatch"/> ticks: the rest of
    /// the time it was busy, which <see cref="CpuMeter"/> counts, since the scheduler counts
    /// the loop's thread (the one the kernel booted on) as its idle thread.
    /// </summary>
    public static long SleptTime { get; private set; }

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

    /// <summary>
    /// Asks for the next iteration at once instead of idling: for a process with more work than
    /// one tick should do (a script running a few lines a tick), which would otherwise go at
    /// the idle pace. Work posted to the loop (an awaited task finishing) needs no asking.
    /// </summary>
    public static void StayAwake()
    {
        stayAwake = true;
    }

    /// <summary>
    /// Ends an iteration: unless work was posted or someone asked to stay awake, sleeps until
    /// <see cref="IdleIterationMilliseconds"/> have passed since it began. Sleeping hands the
    /// processor to the scheduler, which halts it while no other thread has anything to do.
    /// </summary>
    internal static void Idle()
    {
        bool busy = stayAwake;
        stayAwake = false;
        lock (pendingLock)
        {
            busy |= pending.Count > 0;
        }

        if (!busy && iterationStart != 0)
        {
            long elapsed = (Stopwatch.GetTimestamp() - iterationStart) * 1000 / Stopwatch.Frequency;
            if (elapsed < IdleIterationMilliseconds)
            {
                long asleep = Stopwatch.GetTimestamp();
                Thread.Sleep((int)(IdleIterationMilliseconds - elapsed));
                SleptTime += Stopwatch.GetTimestamp() - asleep;
            }
        }

        iterationStart = Stopwatch.GetTimestamp();
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
    /// One iteration of the loop, called over and over by the kernel: the work posted so far,
    /// then every process's tick, then writing the system log when it is due.
    /// </summary>
    internal static void RunOnce()
    {
        RunPending();
        ProcessManager.TickAllProcesses();
        SystemLog.FlushIfDue();
    }

    /// <summary>
    /// Runs the work posted so far. Work posted while it runs waits for the next iteration, so a
    /// callback that posts again cannot keep the loop here.
    /// </summary>
    private static void RunPending()
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
