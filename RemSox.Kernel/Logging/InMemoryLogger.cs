using System.Collections.Concurrent;

namespace RemSox.Kernel.Logging;

public class InMemoryLogger : ILogger
{
    private readonly ConcurrentBag<LogEntry> logs = [];

    public void Log(string message, LogSeverity severity)
    {
        logs.Add(new LogEntry(message, severity, DateTimeOffset.UtcNow));
    }

    public void LogError(string message)
    {
        Log(message, LogSeverity.Error);
    }

    public void LogInfo(string message)
    {
        Log(message, LogSeverity.Info);
    }

    public void LogWarning(string message)
    {
        Log(message, LogSeverity.Warning);
    }

    public IEnumerable<LogEntry> GetLogs(int? count = null)
    {
        IEnumerable<LogEntry> orderedLogs = logs.OrderBy(l => l.Timestamp);
        return count.HasValue ? orderedLogs.TakeLast(count.Value) : orderedLogs;
    }
}