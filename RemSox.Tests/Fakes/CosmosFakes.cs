// Stand-ins for the Cosmos-bound types that the linked kernel files touch, with only the members
// they use. Tests run without a disk and without processes, as a kernel that found no disk would.

namespace Cosmos.Kernel.System.Diagnostics
{
    /// <summary>The serial port: dropped in tests.</summary>
    public static class Log
    {
        public static void WriteString(string text)
        {
        }
    }
}

namespace RemSox.Kernel.FileSystem
{
    /// <summary>No disk is mounted, so nothing in the tests reads or writes the real drive's root.</summary>
    public static class FileSystemManager
    {
        public const string MountPoint = "/";

        public const string NoDiskMessage = "No disk is mounted.";

        public static bool IsMounted => false;
    }
}

namespace RemSox.Kernel.Processing
{
    using RemSox.Kernel.Accounts;

    /// <summary>A process, as far as logon sessions look at one.</summary>
    public abstract class Process
    {
        public int Id { get; init; }

        public LogonSession Session { get; init; } = LogonSessions.System;
    }

    /// <summary>No processes run in tests.</summary>
    public static class ProcessManager
    {
        public static IEnumerable<Process> GetAllProcesses()
        {
            return [];
        }

        public static void StopProcess(int id)
        {
        }

        public static void TickAllProcesses()
        {
        }
    }
}

namespace RemSox.Kernel.Programs
{
    using RemSox.Kernel.Accounts;

    /// <summary>Accounts get no startup script in tests (there is no disk to write it to).</summary>
    public static class UserStartup
    {
        public static void WriteDefault(Account account)
        {
        }
    }
}
