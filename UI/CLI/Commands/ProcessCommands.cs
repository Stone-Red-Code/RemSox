using RemSox.Processes;
using RemSox.Processing;

namespace RemSox.UI.CLI.Commands;

public sealed class SpawnProcessCommand : ICommand
{
    public string Name => "spawn";

    public string Description => "Spawn a new process";

    public void Execute(string? arguments, Action<string> printLine)
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
    }
}

public sealed class ListProcessesCommand : ICommand
{
    public string Name => "ps";

    public string Description => "List running processes";

    public void Execute(string? arguments, Action<string> printLine)
    {
        IEnumerable<Process> processes = ProcessManager.GetAllProcesses();

        printLine("Running processes:");

        foreach (Process process in processes)
        {
            printLine($"  ID: {process.Id}, Name: {process.Name}");
        }
    }
}

public sealed class StopProcessCommand : ICommand
{
    public string Name => "stop";

    public string Description => "Stop a process by ID";

    public void Execute(string? arguments, Action<string> printLine)
    {
        string? idText = arguments;

        if (string.IsNullOrWhiteSpace(idText))
        {
            printLine("Usage: stop <process-id>");
            return;
        }

        if (int.TryParse(idText, out int processId))
        {
            ProcessManager.StopProcess(processId);
            printLine($"Stopped process with ID {processId}");
            return;
        }

        printLine("Invalid process ID");
    }
}