using RemSox.Logging;
using RemSox.Processing;

namespace RemSox.UI.CLI.Commands;

public class ViewProcessLogs : ICommand
{
    public string Name => "logs";

    public string Description => "View logs for a process";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        if (int.TryParse(arguments, out int processId))
        {
            IEnumerable<LogEntry> logs = ProcessManager.GetProcessLogs(processId);
            PrintLogs(logs, printLine);
        }
        else
        {
            IEnumerable<LogEntry> logs = ProcessManager.GetLogs();
            PrintLogs(logs, printLine);
        }
        return Task.CompletedTask;
    }

    private static void PrintLogs(IEnumerable<LogEntry> logs, Action<string> printLine)
    {
        if (!logs.Any())
        {
            printLine("No logs found!");
            return;
        }

        foreach (LogEntry log in logs)
        {
            printLine($"[{log.Timestamp:HH:mm:ss}] [{log.Severity}] {log.Message}");
        }
    }
}