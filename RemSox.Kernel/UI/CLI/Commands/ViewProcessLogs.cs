using RemSox.Kernel.Logging;
using RemSox.Kernel.Processing;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class ViewProcessLogs : ICommand
{
    public string Name => "logs";

    public string Description => "Show the logs of one process, or of all";

    public string Usage => "logs [process-id]";

    public int MaxArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        IEnumerable<LogEntry> logs;

        if (context.Args.Count == 1)
        {
            if (!int.TryParse(context.Args[0], out int processId))
            {
                return Task.FromResult(context.UsageError($"Not a process ID: {context.Args[0]}"));
            }

            logs = ProcessManager.GetProcessLogs(processId);
        }
        else
        {
            logs = ProcessManager.GetLogs();
        }

        if (!logs.Any())
        {
            context.Output.WriteLine("No logs found.");
            return Task.FromResult(CommandResult.Success);
        }

        foreach (LogEntry log in logs)
        {
            string line = $"[{log.Timestamp:HH:mm:ss}] [{log.Severity}] {log.Message}";

            if (log.Severity == LogSeverity.Error)
            {
                context.Output.WriteError(line);
            }
            else
            {
                context.Output.WriteLine(line);
            }
        }

        return Task.FromResult(CommandResult.Success);
    }
}
