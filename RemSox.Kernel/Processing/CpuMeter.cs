using Cosmos.Kernel.System.Diagnostics;

using System.Diagnostics;

namespace RemSox.Kernel.Processing;

/// <summary>
/// Measures how busy the processor is: the share of the time since the last reading that it
/// ran anything rather than halting. The main loop idles between iterations
/// (<see cref="MainLoop.Idle"/>), so this is real usage.
/// </summary>
/// <remarks>
/// It adds two figures. The main loop's thread is the one the kernel booted on, which Cosmos's
/// scheduler takes as its idle thread and so never counts: its busy time is the time it did
/// not sleep (<see cref="MainLoop.SleptTime"/>). Every other thread (Telnet sessions,
/// background work) runs only while the loop sleeps, and the scheduler counts those
/// (<see cref="SchedulerDiagnostics.BusyCpuTimeNs"/>). Each meter keeps its own last reading,
/// so several can measure over different periods.
/// </remarks>
public sealed class CpuMeter
{
    private long lastTime;
    private long lastSleptTime;
    private ulong lastBusyNs;

    /// <summary>
    /// Returns the percentage of the time since the last call (0 to 100) the processor was
    /// busy. The first call gives 0 and starts the period.
    /// </summary>
    public double Measure()
    {
        long now = Stopwatch.GetTimestamp();
        long slept = MainLoop.SleptTime;
        ulong busyNs = SchedulerDiagnostics.BusyCpuTimeNs;

        long elapsed = now - lastTime;
        bool first = lastTime == 0;
        double percent = 0;
        if (!first && elapsed > 0)
        {
            double elapsedNs = elapsed * (1_000_000_000.0 / Stopwatch.Frequency);
            double loop = (double)(elapsed - (slept - lastSleptTime)) / elapsed;
            double otherThreads = (busyNs - lastBusyNs) / (elapsedNs * Math.Max(1u, SchedulerDiagnostics.CpuCount));
            percent = Math.Clamp(100.0 * (loop + otherThreads), 0, 100);
        }

        lastTime = now;
        lastSleptTime = slept;
        lastBusyNs = busyNs;
        return percent;
    }
}
