using System.Collections.Concurrent;
using System.Text;

namespace RemSox.Kernel.Logging;

/// <summary>
/// The system log: every process's log lines, the last <see cref="MaxMemoryEntries"/> kept in
/// memory for <c>logs</c> and all appended to a file (the <c>log.file</c> setting) so they
/// survive a reboot.
/// </summary>
/// <remarks>
/// Logging only queues the line, from any thread. The main loop writes the queue to the file every
/// few seconds (<see cref="FlushIfDue"/>), and shutdown and reboot write it at once
/// (<see cref="Flush"/>). Lines logged at boot, before the settings say where the file is, wait in
/// the queue. When the file passes <see cref="MaxFileSize"/> it becomes <c>.1</c> and a new one starts.
/// </remarks>
public static class SystemLog
{
    private const long MaxFileSize = 512 * 1024;
    private const int MaxPendingLines = 2000;
    private const long FlushIntervalMs = 2000;
    private const int MaxMemoryEntries = 1000;

    // Oldest first; the oldest entry is dropped once it is full.
    private static readonly Queue<LogEntry> memory = new();
    private static readonly Lock memoryLock = new();
    private static readonly ConcurrentQueue<string> pending = new();
    private static readonly Lock fileLock = new();

    private static string? filePath;
    private static bool fileConfigured;
    private static long lastFlush;

    /// <summary>Gets the path the log is written to, or null when writing it is off.</summary>
    public static string? FilePath => filePath;

    /// <summary>Logs a line, tagged with the process that logged it and its account (null for the system).</summary>
    public static void Log(string message, LogSeverity severity, int? processId = null, int? accountId = null)
    {
        LogEntry entry = new(message, severity, DateTimeOffset.UtcNow, processId, accountId);

        lock (memoryLock)
        {
            memory.Enqueue(entry);
            if (memory.Count > MaxMemoryEntries)
            {
                _ = memory.Dequeue();
            }
        }

        if (!fileConfigured || filePath is not null)
        {
            pending.Enqueue($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{severity}] {message}");

            while (pending.Count > MaxPendingLines)
            {
                _ = pending.TryDequeue(out _);
            }
        }
    }

    /// <summary>Returns the lines still in memory, oldest first: all of them, or only one process's.</summary>
    public static List<LogEntry> GetLogs(int? processId = null)
    {
        lock (memoryLock)
        {
            return [.. memory.Where(entry => processId is null || entry.ProcessId == processId)];
        }
    }

    /// <summary>Sets the file the log is written to; null stops writing it.</summary>
    public static void SetFile(string? path)
    {
        Flush();

        lock (fileLock)
        {
            filePath = path;
            fileConfigured = true;

            if (path is null)
            {
                pending.Clear();
            }
        }
    }

    /// <summary>Writes the queued lines when the last write is a few seconds old. Called by the main loop.</summary>
    public static void FlushIfDue()
    {
        long now = Environment.TickCount64;
        if (now - lastFlush >= FlushIntervalMs)
        {
            lastFlush = now;
            Flush();
        }
    }

    /// <summary>Writes the queued lines to the file now.</summary>
    public static void Flush()
    {
        lock (fileLock)
        {
            if (filePath is null || pending.IsEmpty)
            {
                return;
            }

            StringBuilder text = new();
            while (pending.TryDequeue(out string? line))
            {
                _ = text.Append(line).Append('\n');
            }

            try
            {
                _ = Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                FileInfo file = new(filePath);
                if (file.Exists && file.Length + text.Length > MaxFileSize)
                {
                    File.Move(filePath, filePath + ".1", overwrite: true);
                }

                File.AppendAllText(filePath, text.ToString());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // The disk is the problem, so say so on the serial port; the lines are dropped
                // rather than queued forever.
                Cosmos.Kernel.System.Diagnostics.Log.WriteString($"[RemSox] Could not write the log to {filePath}: {ex.Message}{Environment.NewLine}");
            }
        }
    }
}
