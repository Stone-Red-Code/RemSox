namespace RemSox.Kernel.Logging;

/// <summary>One line of the system log; <paramref name="ProcessId"/> is the process that logged it, or null for the system.</summary>
public record LogEntry(string Message, LogSeverity Severity, DateTimeOffset Timestamp, int? ProcessId);
