using RemSox.Kernel.Processes;
using RemSox.Kernel.Processing;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class SpawnProcessCommand : ICommand
{
    public string Name => "spawn";

    public string Description => "Spawn a new process";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        arguments = arguments?.Trim();

        int? processId = arguments switch
        {
            "test" => ProcessManager.SpawnProcess<TestProcess>(),
            "terminal" => ProcessManager.SpawnProcess<TerminalProcess>(),
            _ => null
        };

        if (processId is not null)
        {
            printLine($"Spawned process with ID {processId}");
        }
        else
        {
            printLine("Usage: spawn <process-name>");
            printLine("Available processes: test, terminal");
        }
        return Task.CompletedTask;
    }
}

public sealed class ListProcessesCommand : ICommand
{
    public string Name => "ps";

    public string Description => "List running processes";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        IEnumerable<Process> processes = ProcessManager.GetAllProcesses();

        printLine("Running processes:");

        foreach (Process process in processes)
        {
            printLine($"  ID: {process.Id}, Name: {process.Name}");
        }

        return Task.CompletedTask;
    }
}

public sealed class StopProcessCommand : ICommand
{
    public string Name => "stop";

    public string Description => "Stop a process by ID";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        string? idText = arguments;

        if (string.IsNullOrWhiteSpace(idText))
        {
            printLine("Usage: stop <process-id>");
            return Task.CompletedTask;
        }

        if (int.TryParse(idText, out int processId))
        {
            ProcessManager.StopProcess(processId);
            printLine($"Stopped process with ID {processId}");
            return Task.CompletedTask;
        }

        printLine("Invalid process ID");
        return Task.CompletedTask;
    }
}