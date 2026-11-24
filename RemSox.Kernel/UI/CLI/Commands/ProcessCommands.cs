using RemSox.Kernel.Processing;
using RemSox.Kernel.Programs;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class ListProcessesCommand : ICommand
{
    public string Name => "ps";

    public string Description => "List running processes and whom they run as";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        context.Console.WriteLine("Running processes:");

        foreach (Process process in ProcessManager.GetAllProcesses())
        {
            context.Console.WriteLine($"  ID: {process.Id}, Name: {process.Name}, Account: {process.Session.Account.Name}");
        }

        return Task.FromResult(CommandResult.Success);
    }
}

public sealed class StopProcessCommand : ICommand
{
    public string Name => "stop";

    public string Description => "Stop a process by ID, or every running copy of a program by name (others' and services with admin)";

    public string Usage => "stop <process-id | program>";

    public int MinArguments => 1;

    public int MaxArguments => 1;

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        string target = context.Args[0];

        if (int.TryParse(target, out int processId))
        {
            if (ProcessManager.GetProcess(processId) is not { } process)
            {
                return context.Fail($"No process with ID {processId}");
            }

            if (process.DenyStop(context.Caller) is { } denied)
            {
                return context.Fail(denied);
            }

            ProcessManager.StopProcess(processId);
            context.Console.WriteLine($"Stopped process with ID {processId}");
            return CommandResult.Success;
        }

        if (ProgramRegistry.Find(target) is not { } program)
        {
            return context.UsageError($"Not a process ID or program: {target}");
        }

        List<Process> running = [.. ProgramRegistry.GetProcesses(program)];
        if (running.Count == 0)
        {
            return context.Fail($"{program.Name} is not running.");
        }

        foreach (Process process in running)
        {
            if (process.DenyStop(context.Caller) is { } denied)
            {
                return context.Fail(denied);
            }
        }

        // Waits, so the desktop has given the screen back to the console before the next prompt.
        foreach (Process process in running)
        {
            await ProcessManager.StopProcessAndWaitAsync(process.Id);
        }

        context.Console.WriteLine(running.Count == 1 ? $"Stopped {program.Name}." : $"Stopped {running.Count} {program.Name} processes.");
        return CommandResult.Success;
    }
}
