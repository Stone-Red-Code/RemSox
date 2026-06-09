using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RemSox.Logging
{
    public class ProxyLogger(IEnumerable<ILogger> loggers) : ILogger
    {
        public void Log(string message, LogSeverity severity)
        {
            foreach (var logger in loggers)
            {
                logger.Log(message, severity);
            }
        }

        public void LogError(string message)
        {
            Log(message, LogSeverity.Error);
        }

        public void LogInfo(string message)
        {
            Log(message, LogSeverity.Info);
        }

        public void LogWarning(string message)
        {
            Log(message, LogSeverity.Warning);
        }

        public IEnumerable<LogEntry> GetLogs(int? count = null)
        {
            return loggers.SelectMany(logger => logger.GetLogs(count / loggers.Count())).OrderBy(entry => entry.Timestamp);
        }
    }
}