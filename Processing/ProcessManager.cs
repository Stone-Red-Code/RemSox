using System;

namespace RemSox.Processing;

public static class ProcessManager
{
    private static readonly Dictionary<int, (Process Process, Thread Thread)> processes = [];

    private static int nextProcessId = 0;

    public static int SpawnProcess<T>() where T : Process, new()
    {
        int id = GetNextProcessId();

        T process = new()
        {
            Id = id
        };

        Thread thread = new(process.Run);

        processes.Add(id, (process, thread));

        thread.Start();

        return id;
    }

    public static void StopProcess(int processId)
    {
        if (!processes.TryGetValue(processId, out var entry))
            return;

        entry.Process.RequestStop();

        processes.Remove(processId);
    }

    public static void StopAllProcesses()
    {
        foreach (var entry in processes.Values)
        {
            entry.Process.RequestStop();
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
