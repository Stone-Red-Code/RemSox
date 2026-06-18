namespace RemSox.Kernel.Logging;

public record LogEntry(string Message, LogSeverity Severity, DateTimeOffset Timestamp);