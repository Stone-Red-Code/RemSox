using System.Diagnostics;

using RemSox.Kernel.UI.GUI;

namespace RemSox.Kernel.Processing;

// The main loop's idling, apart from the rest since it needs Cosmos and the display, which the
// tests (linking MainLoop.cs) do without.
public sealed partial class MainLoop
{
    /// <summary>
    /// How long an iteration lasts at least when nothing happens, in milliseconds. The
    /// scheduler's timer ticks every 10 ms, so the loop wakes on every second tick: about 50
    /// idle iterations a second, for what processes poll (the network, a clock).
    /// </summary>
    private const int IdleIterationMilliseconds = 15;

    // When the current iteration began (a Stopwatch timestamp), and whether something asked
    // for the next one at once.
    private static long iterationStart;
    private static bool stayAwake;

    /// <summary>
    /// Gets how long the loop halted since boot, in <see cref="Stopwatch"/> ticks: the rest of
    /// the time it was busy, which <see cref="CpuMeter"/> counts, since the scheduler counts
    /// the loop's thread (the one the kernel booted on) as its idle thread.
    /// </summary>
    public static long SleptTime { get; private set; }

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
    /// Ends an iteration: halts the processor, an interrupt at a time, until
    /// <see cref="IdleIterationMilliseconds"/> have passed since it began, but goes on at once
    /// when there is something to do: work posted to the loop, a process that asked to stay
    /// awake, or input from the mouse or keyboard (<see cref="LocalDisplay.HasInput"/>), whose
    /// interrupt wakes the processor. Moving the pointer thus runs an iteration for each move
    /// the mouse reports, while an idle desktop costs only about 50 iterations a second.
    /// </summary>
    /// <remarks>
    /// <see cref="Thread.Sleep(int)"/> would wake only on the scheduler's 10 ms ticks, and
    /// after two of them for any wait, so the pointer moved in jerks. Other threads still run:
    /// the timer's interrupt lets the scheduler switch to them, and the loop's thread is its
    /// idle thread, which runs only when they wait.
    /// </remarks>
    internal static void Idle()
    {
        bool awake = stayAwake;
        stayAwake = false;
        long idleUntil = iterationStart + (IdleIterationMilliseconds * Stopwatch.Frequency / 1000);
        while (!awake && !HasPosted() && !LocalDisplay.HasInput && Stopwatch.GetTimestamp() < idleUntil)
        {
            long asleep = Stopwatch.GetTimestamp();
            Cosmos.Kernel.System.Power.Halt();
            SleptTime += Stopwatch.GetTimestamp() - asleep;
        }

        iterationStart = Stopwatch.GetTimestamp();
    }

    private static bool HasPosted()
    {
        lock (pendingLock)
        {
            return pending.Count > 0;
        }
    }
}
