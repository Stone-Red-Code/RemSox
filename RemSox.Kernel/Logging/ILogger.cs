namespace RemSox.Kernel.Logging;

/// <summary>Where a process writes its log lines; see <see cref="ProcessLogger"/>.</summary>
public interface ILogger
{
    void Log(string message, LogSeverity severity);
}
