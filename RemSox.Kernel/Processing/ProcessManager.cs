using RemSox.Kernel.Accounts;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;
using RemSox.Kernel.UI.GUI.Windows;

using System.Diagnostics;

namespace RemSox.Kernel.Processing;

public static class ProcessManager
{
    // Touched only on the main loop, like everything a process owns.
    private static readonly Dictionary<int, (Process Process, ProcessMetrics Metrics, TaskCompletionSource ExitSource)> processes = [];

    private static int nextProcessId = 0;

    private static int nextSystemProcessId = -1;

    /// <summary>
    /// Creates a process, starts it and adds it to those the main loop ticks.
    /// </summary>
    /// <param name="args">The process's start arguments.</param>
    /// <param name="workingDirectory">
    /// The folder the process starts in: the starter's own, so a program started from a shell
    /// finds files relative to that shell. Null gives the system's default folder (home, or
    /// <c>/</c> without a disk), for services and anything not started from a folder.
    /// </param>
    /// <returns>
    /// The new process's ID, or null when its <c>Start</c> threw: the exception is logged under
    /// that ID, the windows it opened are closed and it is never ticked.
    /// </returns>
    public static int? SpawnProcess<T>(string[]? args = null, string? workingDirectory = null, LogonSession? session = null, bool elevated = false) where T : Process, new()
    {
        MainLoop.AssertMainThread(nameof(SpawnProcess));

        if (ProcessManifest.HasFlag<T>(ProcessManifest.ProcessManifestFlags.Singleton) && IsProcessRunning<T>())
        {
            throw new InvalidOperationException($"An instance of process type {typeof(T).Name} is already running.");
        }

        int id = ProcessManifest.HasFlag<T>(ProcessManifest.ProcessManifestFlags.System) ? nextSystemProcessId-- : nextProcessId++;

        T process = new()
        {
            Id = id,
            Logger = new ProcessLogger(id, (session ?? LogonSessions.System).Account.Id),
            WorkingDirectory = workingDirectory ?? GetDefaultWorkingDirectory(session),
            Session = session ?? LogonSessions.System,
            Elevated = elevated,
        };

        try
        {
            process.Start(args ?? []);
        }
        catch (Exception ex)
        {
            SystemLog.Log($"Process {process.Name} (ID: {id}) failed to start: {ex}", LogSeverity.Error, id);
            WindowManager.CloseWindowsForProcess(id);
            ListeningPorts.Release(process);
            return null;
        }

        _ = processes.TryAdd(id, (process, new ProcessMetrics(), new TaskCompletionSource()));
        SystemLog.Log($"Started process {process.Name} ({typeof(T).Name}) with ID {id}.", LogSeverity.Info, id);

        return id;
    }

    /// <summary>
    /// The session's home folder, or the root for the system and without a disk. Named outright
    /// rather than read from the kernel's global current directory, which nothing should depend on.
    /// </summary>
    private static string GetDefaultWorkingDirectory(LogonSession? session)
    {
        return FileSystemManager.IsMounted && session is { IsSystem: false } ? session.Account.HomeFolder : FileSystemManager.MountPoint;
    }

    /// <summary>Asks a process to stop; the main loop calls its <c>Stop</c> on the next tick.</summary>
    public static void StopProcess(int processId)
    {
        if (processes.TryGetValue(processId, out (Process Process, ProcessMetrics Metrics, TaskCompletionSource ExitSource) entry))
        {
            SystemLog.Log($"Requesting stop of process {entry.Process.Name} (ID: {processId}).", LogSeverity.Info, processId);
            entry.Process.RequestStop();
        }
    }

    /// <summary>Asks a process to stop and completes once it has.</summary>
    public static async Task StopProcessAndWaitAsync(int processId)
    {
        if (processes.TryGetValue(processId, out (Process Process, ProcessMetrics Metrics, TaskCompletionSource ExitSource) entry))
        {
            StopProcess(processId);
            await entry.ExitSource.Task;
        }
    }

    /// <summary>Completes once the process has stopped (at once if there is no such process).</summary>
    public static async Task WaitForProcessExitAsync(int processId)
    {
        if (processes.TryGetValue(processId, out (Process Process, ProcessMetrics Metrics, TaskCompletionSource ExitSource) entry))
        {
            await entry.ExitSource.Task;
        }
    }

    public static Process? GetProcess(int processId)
    {
        return processes.TryGetValue(processId, out (Process Process, ProcessMetrics Metrics, TaskCompletionSource ExitSource) entry) ? entry.Process : null;
    }

    /// <summary>Returns a running process's tick times, or null when there is no such process.</summary>
    public static ProcessMetrics? GetMetrics(int processId)
    {
        return processes.TryGetValue(processId, out (Process Process, ProcessMetrics Metrics, TaskCompletionSource ExitSource) entry) ? entry.Metrics : null;
    }

    public static bool IsProcessRunning<T>() where T : Process
    {
        return GetProcessesOfType<T>().Any();
    }

    public static IEnumerable<T> GetProcessesOfType<T>() where T : Process
    {
        return GetAllProcesses().OfType<T>();
    }

    public static IEnumerable<Process> GetAllProcesses()
    {
        return [.. processes.Values.Select(entry => entry.Process)];
    }

    internal static void TickAllProcesses()
    {
        // A snapshot: a tick may start or stop processes.
        foreach ((Process process, ProcessMetrics metrics, TaskCompletionSource _) in processes.Values.ToList())
        {
            if (!process.IsRunning)
            {
                CleanupProcess(process);
                continue;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                process.Tick();
            }
            catch (Exception ex)
            {
                SystemLog.Log($"Process {process.Name} (ID: {process.Id}) threw an exception during Tick: {ex}", LogSeverity.Error, process.Id);
            }

            int tickTimeMs = (int)stopwatch.ElapsedMilliseconds;
            metrics.LastTickTimeMs = tickTimeMs;
            metrics.AverageTickTimeMs = ((metrics.AverageTickTimeMs * 7) + tickTimeMs) / 8;
        }
    }

    private static void CleanupProcess(Process process)
    {
        process.Stop();
        ListeningPorts.Release(process);

        if (processes.Remove(process.Id, out (Process Process, ProcessMetrics Metrics, TaskCompletionSource ExitSource) entry))
        {
            _ = entry.ExitSource.TrySetResult();
        }

        WindowManager.CloseWindowsForProcess(process.Id);

        SystemLog.Log($"Process {process.Name} (ID: {process.Id}) has stopped.", LogSeverity.Info, process.Id);
    }
}
