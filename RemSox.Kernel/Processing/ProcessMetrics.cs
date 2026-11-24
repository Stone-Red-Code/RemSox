using System.Diagnostics;

namespace RemSox.Kernel.Processing;

/// <summary>
/// How long a process's ticks take, for the Task Manager and <c>ps</c> to show. Measured with
/// <see cref="Stopwatch"/> timestamps, since most ticks take well under a millisecond.
/// </summary>
public sealed class ProcessMetrics
{
    /// <summary>Gets the time all the process's ticks took so far, in <see cref="Stopwatch"/> ticks; the difference between two readings is its share of that time.</summary>
    public long TotalTime { get; private set; }

    /// <summary>Gets how long its last tick took, in microseconds.</summary>
    public double LastTickMicroseconds { get; private set; }

    /// <summary>
    /// Gets how long its ticks take of late, in microseconds: an average leaning on the latest.
    /// A fraction, since many ticks take a few microseconds, which a whole-number average would
    /// round away to nothing.
    /// </summary>
    public double AverageTickMicroseconds { get; private set; }

    /// <summary>Converts <see cref="Stopwatch"/> ticks to microseconds.</summary>
    public static double ToMicroseconds(long stopwatchTicks)
    {
        return stopwatchTicks * 1_000_000.0 / Stopwatch.Frequency;
    }

    /// <summary>Records a tick that took <paramref name="elapsed"/> <see cref="Stopwatch"/> ticks.</summary>
    internal void Record(long elapsed)
    {
        TotalTime += elapsed;
        LastTickMicroseconds = ToMicroseconds(elapsed);
        AverageTickMicroseconds = ((AverageTickMicroseconds * 7) + LastTickMicroseconds) / 8;
    }
}
