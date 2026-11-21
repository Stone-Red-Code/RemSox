namespace RemSox.Kernel.Logging;

/// <summary>
/// A process's logger: its lines go to the system log tagged with its ID and its account, so
/// <c>logs &lt;id&gt;</c> can show them, to that account too, even after the process has ended.
/// </summary>
public sealed class ProcessLogger(int processId, int accountId) : ILogger
{
    public void Log(string message, LogSeverity severity)
    {
        SystemLog.Log(message, severity, processId, accountId);
    }
}
