using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RemSox.Logging
{
    public interface ILogger
    {
        void Log(string message, LogSeverity severity);

        void LogInfo(string message);

        void LogError(string message);

        void LogWarning(string message);

        IEnumerable<LogEntry> GetLogs(int? count = null);
    }
}