namespace RemSox.Kernel.Logging;

/// <summary>
/// A process's logger: its lines go to the system log tagged with its ID, so <c>logs &lt;id&gt;</c>
/// can show them, even after the process has ended.
/// </summary>
public sealed class ProcessLogger(int processId) : ILogger
{
    public void Log(string message, LogSeverity severity)
    {
        SystemLog.Log(message, severity, processId);
    }
}
