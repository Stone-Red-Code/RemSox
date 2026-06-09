using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RemSox.Logging;
using RemSox.Processing;

namespace RemSox.UI.CLI.Commands
{
    public class ViewProcessLogs : ICommand
    {
        public string Name => "logs";

        public string Description => "View logs for a process";

        public void Execute(string? arguments, Action<string> printLine)
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
}