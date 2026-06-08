using System;
using System.Collections.Concurrent;
using RemSox.UI.GUI.Windows;

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
                processes.TryRemove(id, out _);
                WindowManager.CloseWindowsForProcess(id);
            }
        });

        processes.TryAdd(id, (process, thread));

        thread.Start();

        return id;
    }

    public static void StopProcess(int processId)
    {
        if (!processes.TryGetValue(processId, out var entry))
            return;

        entry.Process.RequestStop();

        WindowManager.CloseWindowsForProcess(processId);

        processes.TryRemove(processId, out _);
    }

    public static void StopAllProcesses()
    {
        foreach (var entry in processes.Values)
        {
            StopProcess(entry.Process.Id);
        }

        processes.Clear();
    }

    public static Process? GetProcess(int processId)
    {
        if (processes.TryGetValue(processId, out var entry))
        {
            return entry.Process;
        }

        return null;
    }

    public static IEnumerable<Process> GetAllProcesses()
    {
        foreach (var entry in processes.Values)
        {
            yield return entry.Process;
        }
    }

    public static bool TryGetProcess(int processId, out Process? process)
    {
        if (processes.TryGetValue(processId, out var entry))
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
