namespace RemSox.Logging;

public interface ILogger
{
    void Log(string message, LogSeverity severity);

    void LogInfo(string message);

    void LogError(string message);

    void LogWarning(string message);

    IEnumerable<LogEntry> GetLogs(int? count = null);
}