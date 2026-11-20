namespace RemSox.Kernel.Processing;

/// <summary>How long a process's ticks take, for a task manager or <c>ps</c> to show.</summary>
public sealed class ProcessMetrics
{
    public int AverageTickTimeMs { get; set; }

    public int LastTickTimeMs { get; set; }
}