using RemSox.Kernel.Logging;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class LogsCommand : ICommand
{
    public string Name => "logs";

    public string Description => "Show the logs of one process, or of all";

    public string Usage => "logs [process-id]";

    public int MaxArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        List<LogEntry> logs;

        if (context.Args.Count == 1)
        {
            if (!int.TryParse(context.Args[0], out int processId))
            {
                return Task.FromResult(context.UsageError($"Not a process ID: {context.Args[0]}"));
            }

            logs = SystemLog.GetLogs(processId);
        }
        else
        {
            logs = SystemLog.GetLogs();
        }

        if (logs.Count == 0)
        {
            context.Console.WriteLine("No logs found.");
            return Task.FromResult(CommandResult.Success);
        }

        foreach (LogEntry log in logs)
        {
            string line = $"[{log.Timestamp:HH:mm:ss}] [{log.Severity}] {log.Message}";

            if (log.Severity == LogSeverity.Error)
            {
                context.Console.WriteError(line);
            }
            else
            {
                context.Console.WriteLine(line);
            }
        }

        return Task.FromResult(CommandResult.Success);
    }
}
