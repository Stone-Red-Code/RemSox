using RemSox.UI.GUI.Windows;

using System.Collections.Concurrent;

namespace RemSox.Processing;

public static class ProcessManager
{
    private static readonly ConcurrentDictionary<int, (Process Process, Thread Thread)> processes = new();

    private static readonly ConcurrentDictionary<Type, ConcurrentHashSet<int>> processesByType = new();

    private static int nextProcessId = 0;

    private static int nextSystemProcessId = -1;

    public static int SpawnProcess<T>(string[]? args = null) where T : Process, new()
    {
        if (ProcessManifest.HasFlag<T>(ProcessManifest.ProcessManifestFlags.Singleton) && IsProcessRunning<T>())
        {
            throw new InvalidOperationException($"An instance of process type {typeof(T).Name} is already running.");
        }

        int id = ProcessManifest.HasFlag<T>(ProcessManifest.ProcessManifestFlags.System) ? GetNextSystemProcessId() : GetNextProcessId();

        T process = new()
        {
            Id = id
        };

        processesByType.AddOrUpdate(typeof(T), _ => [id], (_, set) =>
        {
            set.Add(id);
            return set;
        });

        Thread thread = new(() =>
        {
            try
            {
                process.Run(args ?? []);
            }
            finally
            {
                processes.TryRemove(id, out _);

                if (processesByType.TryGetValue(typeof(T), out var set))
                {
                    set.TryRemove(id);
                    if (set.Count == 0)
                        processesByType.TryRemove(typeof(T), out _);
                }

                WindowManager.CloseWindowsForProcess(id);
            }
        });

        processes.TryAdd(id, (process, thread));
        thread.Start();

        return id;
    }

    public static void StopProcess(int processId, bool waitForExit = false)
    {
        if (!processes.TryGetValue(processId, out (Process Process, Thread Thread) entry))
        {
            return;
        }

        entry.Process.RequestStop();
    }

    public static async Task StopProcessAndWaitAsync(int processId)
    {
        if (!processes.TryGetValue(processId, out (Process Process, Thread Thread) entry))
        {
            return;
        }

        entry.Process.RequestStop();
        await Task.Run(() => entry.Thread.Join());
    }

    public static void StopAllProcesses()
    {
        foreach ((Process Process, Thread Thread) entry in processes.Values)
        {
            StopProcess(entry.Process.Id);
        }

        processes.Clear();
    }

    public static Process? GetProcess(int processId)
    {
        if (processes.TryGetValue(processId, out (Process Process, Thread Thread) entry))
        {
            return entry.Process;
        }

        return null;
    }

    public static bool IsProcessRunning<T>() where T : Process
    {
        return processesByType.TryGetValue(typeof(T), out var set) && set.Count > 0;
    }

    public static IEnumerable<T> GetProcessesOfType<T>() where T : Process
    {
        if (processesByType.TryGetValue(typeof(T), out var set))
        {
            foreach (int processId in set)
            {
                if (processes.TryGetValue(processId, out (Process Process, Thread Thread) entry) && entry.Process is T typedProcess)
                {
                    yield return typedProcess;
                }
            }
        }
    }

    public static IEnumerable<Process> GetAllProcesses()
    {
        foreach ((Process Process, Thread Thread) entry in processes.Values)
        {
            yield return entry.Process;
        }
    }

    public static bool TryGetProcess(int processId, out Process? process)
    {
        if (processes.TryGetValue(processId, out (Process Process, Thread Thread) entry))
        {
            process = entry.Process;
            return true;
        }

        process = null;
        return false;
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
