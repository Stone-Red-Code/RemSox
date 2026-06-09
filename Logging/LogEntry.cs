using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RemSox.Logging
{
    public record LogEntry(string Message, LogSeverity Severity, DateTimeOffset Timestamp);
}