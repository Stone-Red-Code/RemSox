using RemSox.Kernel.Processes;
using RemSox.Kernel.Processing;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class SpawnProcessCommand : ICommand
{
    public string Name => "spawn";

    public string Description => "Start a process (test or terminal)";

    public string Usage => "spawn <test|terminal>";

    public int MinArguments => 1;

    public int MaxArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        int? processId = context.Args[0] switch
        {
            "test" => ProcessManager.SpawnProcess<TestProcess>(workingDirectory: context.Session.WorkingDirectory),
            "terminal" => ProcessManager.SpawnProcess<TerminalProcess>(workingDirectory: context.Session.WorkingDirectory),
            _ => null
        };

        if (processId is null)
        {
            return Task.FromResult(context.UsageError($"Unknown process: {context.Args[0]}"));
        }

        context.Console.WriteLine($"Spawned process with ID {processId}");
        return Task.FromResult(CommandResult.Success);
    }
}

public sealed class ListProcessesCommand : ICommand
{
    public string Name => "ps";

    public string Description => "List running processes";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        context.Console.WriteLine("Running processes:");

        foreach (Process process in ProcessManager.GetAllProcesses())
        {
            context.Console.WriteLine($"  ID: {process.Id}, Name: {process.Name}");
        }

        return Task.FromResult(CommandResult.Success);
    }
}

public sealed class StopProcessCommand : ICommand
{
    public string Name => "stop";

    public string Description => "Stop a process by ID";

    public string Usage => "stop <process-id>";

    public int MinArguments => 1;

    public int MaxArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        if (!int.TryParse(context.Args[0], out int processId))
        {
            return Task.FromResult(context.UsageError($"Not a process ID: {context.Args[0]}"));
        }

        if (!ProcessManager.TryGetProcess(processId, out _))
        {
            return Task.FromResult(context.Fail($"No process with ID {processId}"));
        }

        ProcessManager.StopProcess(processId);
        context.Console.WriteLine($"Stopped process with ID {processId}");
        return Task.FromResult(CommandResult.Success);
    }
}
