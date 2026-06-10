using RemSox.Logging;
using RemSox.UI.GUI.Windows;

using System.Collections.Concurrent;
using System.Diagnostics;

namespace RemSox.Processing;

public static class ProcessManager
{
    private static readonly ConcurrentDictionary<int, (Process Process, ProcessMetrics Metrics)> processes = new();

    private static readonly ConcurrentDictionary<Type, ConcurrentHashSet<int>> processesByType = new();

    private static readonly InMemoryLogger logger = new();
    private static readonly ConcurrentDictionary<int, InMemoryLogger> processLoggers = new();

    private static int nextProcessId = 0;

    private static int nextSystemProcessId = -1;

    public static int SpawnProcess<T>(string[]? args = null) where T : Process, new()
    {
        logger.Log($"Attempting to spawn process of type {typeof(T).Name}...", LogSeverity.Info);

        if (ProcessManifest.HasFlag<T>(ProcessManifest.ProcessManifestFlags.Singleton) && IsProcessRunning<T>())
        {
            logger.Log($"Cannot spawn process of type {typeof(T).Name} because it is marked as a singleton and an instance is already running.", LogSeverity.Warning);
            throw new InvalidOperationException($"An instance of process type {typeof(T).Name} is already running.");
        }

        int id = ProcessManifest.HasFlag<T>(ProcessManifest.ProcessManifestFlags.System) ? GetNextSystemProcessId() : GetNextProcessId();

        InMemoryLogger processLogger = new();
        ProxyLogger proxyLogger = new([logger, processLogger]);

        _ = processLoggers.TryAdd(id, processLogger);

        T process = new()
        {
            Id = id,
            Logger = proxyLogger
        };

        _ = processesByType.AddOrUpdate(typeof(T), _ => [id], (_, set) =>
        {
            set.Add(id);
            return set;
        });

        try
        {
            process.Start(args ?? []);
        }
        catch (Exception ex)
        {
            logger.Log($"Process {process.Name} (ID: {process.Id}) terminated with an exception: {ex}", LogSeverity.Error);
        }

        _ = processes.TryAdd(id, (process, new ProcessMetrics()));
        logger.Log($"Spawned process {process.Name} of type {typeof(T).Name} with ID {id}.", LogSeverity.Info);

        return id;
    }

    public static void StopProcess(int processId, bool waitForExit = false)
    {
        if (!processes.TryGetValue(processId, out (Process Process, ProcessMetrics Metrics) entry))
        {
            return;
        }

        logger.Log($"Requesting stop of process {entry.Process.Name} (ID: {entry.Process.Id}).", LogSeverity.Info);
        entry.Process.RequestStop();
    }

    public static async Task StopProcessAndWaitAsync(int processId)
    {
        if (!processes.TryGetValue(processId, out (Process Process, ProcessMetrics Metrics) entry))
        {
            return;
        }

        logger.Log($"Requesting stop of process {entry.Process.Name} (ID: {entry.Process.Id}).", LogSeverity.Info);
        entry.Process.RequestStop();
    }

    public static void StopAllProcesses()
    {
        foreach ((Process Process, ProcessMetrics Metrics) entry in processes.Values)
        {
            StopProcess(entry.Process.Id);
        }

        processes.Clear();
    }

    public static Process? GetProcess(int processId)
    {
        if (processes.TryGetValue(processId, out (Process Process, ProcessMetrics Metrics) entry))
        {
            return entry.Process;
        }

        return null;
    }

    public static bool IsProcessRunning<T>() where T : Process
    {
        return processesByType.TryGetValue(typeof(T), out ConcurrentHashSet<int>? set) && set.Count > 0;
    }

    public static IEnumerable<T> GetProcessesOfType<T>() where T : Process
    {
        if (processesByType.TryGetValue(typeof(T), out ConcurrentHashSet<int>? set))
        {
            foreach (int processId in set)
            {
                if (processes.TryGetValue(processId, out (Process Process, ProcessMetrics Metrics) entry) && entry.Process is T typedProcess)
                {
                    yield return typedProcess;
                }
            }
        }
    }

    public static IEnumerable<Process> GetAllProcesses()
    {
        foreach ((Process Process, ProcessMetrics Metrics) entry in processes.Values)
        {
            yield return entry.Process;
        }
    }

    public static bool TryGetProcess(int processId, out Process? process)
    {
        if (processes.TryGetValue(processId, out (Process Process, ProcessMetrics Metrics) entry))
        {
            process = entry.Process;
            return true;
        }

        process = null;
        return false;
    }

    public static IEnumerable<LogEntry> GetLogs(int? count = null)
    {
        return logger.GetLogs(count);
    }

    public static IEnumerable<LogEntry> GetProcessLogs(int processId, int? count = null)
    {
        if (processLoggers.TryGetValue(processId, out InMemoryLogger? processLogger))
        {
            return processLogger.GetLogs(count);
        }

        return [];
    }

    internal static void TickAllProcesses()
    {
        foreach ((Process Process, ProcessMetrics Metrics) entry in processes.Values)
        {
            if (entry.Process.IsRunning)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();

                try
                {
                    entry.Process.Tick();
                }
                catch (Exception ex)
                {
                    logger.Log($"Process {entry.Process.Name} (ID: {entry.Process.Id}) threw an exception during Tick: {ex}", LogSeverity.Error);
                }

                stopwatch.Stop();

                int tickTimeMs = (int)stopwatch.ElapsedMilliseconds;
                entry.Metrics.LastTickTimeMs = tickTimeMs;
                entry.Metrics.AverageTickTimeMs = ((entry.Metrics.AverageTickTimeMs * 7) + tickTimeMs) / 8;
            }
            else
            {
                CleanupProcess(entry.Process);
            }
        }
    }

    private static void CleanupProcess(Process process)
    {
        process.Stop();

        _ = processes.TryRemove(process.Id, out _);

        if (processesByType.TryGetValue(process.GetType(), out ConcurrentHashSet<int>? set))
        {
            _ = set.TryRemove(process.Id);
            if (set.Count == 0)
            {
                _ = processesByType.TryRemove(process.GetType(), out _);
            }
        }

        WindowManager.CloseWindowsForProcess(process.Id);

        logger.Log($"Process {process.Name} (ID: {process.Id}) has stopped.", LogSeverity.Info);

        _ = processLoggers.TryRemove(process.Id, out _);
    }

    private static int GetNextProcessId()
    {
        return nextProcessId++;
    }

    private static int GetNextSystemProcessId()
    {
        return nextSystemProcessId--;
    }
}
