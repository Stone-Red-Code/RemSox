using RemSox.UI.GUI.Windows;

using System.Collections.Concurrent;

namespace RemSox.Processing;

public static class ProcessManager
{
    private static readonly ConcurrentDictionary<int, (Process Process, Thread Thread)> processes = new();

    private static int nextProcessId = 0;

    public static int SpawnProcess<T>() where T : Process, new()
    {
        int id = GetNextProcessId();

        T process = new()
        {
            Id = id
        };

        Thread thread = new(() =>
        {
            try
            {
                process.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Process {process.Name} (ID: {process.Id}) terminated with an exception: {ex}");
            }
            finally
            {
                _ = processes.TryRemove(id, out _);
                WindowManager.CloseWindowsForProcess(id);
            }
        });

        _ = processes.TryAdd(id, (process, thread));

        thread.Start();

        return id;
    }

    public static void StopProcess(int processId)
    {
        if (!processes.TryGetValue(processId, out (Process Process, Thread Thread) entry))
        {
            return;
        }

        entry.Process.RequestStop();

        WindowManager.CloseWindowsForProcess(processId);

        _ = processes.TryRemove(processId, out _);
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
}
