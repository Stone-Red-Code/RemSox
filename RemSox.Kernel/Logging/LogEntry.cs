namespace RemSox.Kernel.Logging;

/// <summary>
/// One line of the system log; <paramref name="ProcessId"/> is the process that logged it, or null
/// for the system, and <paramref name="AccountId"/> the account that process ran as, so users can
/// read their own processes' lines (null for the system's own lines).
/// </summary>
public record LogEntry(string Message, LogSeverity Severity, DateTimeOffset Timestamp, int? ProcessId, int? AccountId = null);
