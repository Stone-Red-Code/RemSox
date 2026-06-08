using System;
using RemSox.Processing;

namespace RemSox.UI.GUI.CLI.Commands;

public sealed class SpawnTestProcessCommand : ICommand
{
    public string Name => "spawn test";

    public string Description => "Spawn the test process";

    public void Execute(string? arguments)
    {
        int processId = ProcessManager.SpawnProcess<TestProcess>();
        Console.WriteLine($"Spawned TestProcess with ID {processId}");
    }
}

public sealed class ListProcessesCommand : ICommand
{
    public string Name => "ps";

    public string Description => "List running processes";

    public void Execute(string? arguments)
    {
        IEnumerable<Process> processes = ProcessManager.GetAllProcesses();

        Console.WriteLine("Running processes:");

        foreach (Process process in processes)
        {
            Console.WriteLine($"  ID: {process.Id}, Name: {process.Name}");
        }
    }
}

public sealed class StopProcessCommand : ICommand
{
    public string Name => "stop";

    public string Description => "Stop a process by ID";

    public void Execute(string? arguments)
    {
        string? idText = arguments;

        if (string.IsNullOrWhiteSpace(idText))
        {
            Console.WriteLine("Usage: stop <process-id>");
            return;
        }

        if (int.TryParse(idText, out int processId))
        {
            ProcessManager.StopProcess(processId);
            Console.WriteLine($"Stopped process with ID {processId}");
            return;
        }

        Console.WriteLine("Invalid process ID");
    }
}