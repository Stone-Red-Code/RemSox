using System.Runtime;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace System.Runtime
{
    /// <summary>
    /// Exports a method under a native symbol name. ILC matches this attribute by name;
    /// Cosmos keeps its own copy internal, so RemSox declares one too.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class RuntimeExportAttribute(string entry) : Attribute
    {
        public string Entry { get; } = entry;
    }
}

namespace RemSox.Kernel.Plugs
{
    /// <summary>
    /// libSystem.Native imports that Cosmos does not provide but that the BCL code compiled into
    /// RemSox references. Every such import is linked statically, so a missing one is a link error
    /// rather than a crash when called. Cosmos 3.0.97 exports the socket and GetUnixRelease imports
    /// itself, so defining them here again fails the link with a duplicate symbol.
    /// </summary>
    /// <remarks>
    /// The file I/O imports are implemented on top of the plugged single-buffer paths. Everything
    /// else sits on code paths RemSox does not use (System.Diagnostics.Process, terminal setup) and
    /// reports failure. Stubs ignore their arguments, which is safe under the
    /// SysV x64 calling convention.
    /// </remarks>
    internal static unsafe class SystemNativeExports
    {
        // Interop.Error values (platform-agnostic PAL errnos).
        private const int Success = 0;
        private const int EBADF = 0x10008;
        private const int EIO = 0x1001D;
        private const int ENOENT = 0x1002D;
        private const int ENOTSUP = 0x1003D;

        [StructLayout(LayoutKind.Sequential)]
        private struct IOVector
        {
            public byte* Base;
            public nuint Count;
        }

        private static int Fail(int error)
        {
            Marshal.SetLastSystemError(error);
            return -1;
        }

        // ---------------- file I/O ----------------

        [RuntimeExport("SystemNative_PReadV")]
        private static long PReadV(nint fd, IOVector* vectors, int vectorCount, long fileOffset)
        {
            try
            {
                SafeFileHandle handle = new(fd, ownsHandle: false);
                long total = 0;

                for (int i = 0; i < vectorCount; i++)
                {
                    int count = (int)vectors[i].Count;
                    int read = RandomAccess.Read(handle, new Span<byte>(vectors[i].Base, count), fileOffset + total);
                    total += read;

                    if (read < count)
                    {
                        break;
                    }
                }

                return total;
            }
            catch (Exception)
            {
                return Fail(EIO);
            }
        }

        [RuntimeExport("SystemNative_PWriteV")]
        private static long PWriteV(nint fd, IOVector* vectors, int vectorCount, long fileOffset)
        {
            try
            {
                SafeFileHandle handle = new(fd, ownsHandle: false);
                long total = 0;

                for (int i = 0; i < vectorCount; i++)
                {
                    int count = (int)vectors[i].Count;
                    RandomAccess.Write(handle, new ReadOnlySpan<byte>(vectors[i].Base, count), fileOffset + total);
                    total += count;
                }

                return total;
            }
            catch (Exception)
            {
                return Fail(EIO);
            }
        }

        [RuntimeExport("SystemNative_Access")]
        private static int Access(byte* path, int mode)
        {
            string? fullPath = Marshal.PtrToStringUTF8((nint)path);
            return fullPath is not null && (File.Exists(fullPath) || Directory.Exists(fullPath)) ? Success : Fail(ENOENT);
        }

        /// <summary>The CoreLib FStat is plugged by Cosmos; this is the copy other BCL assemblies compile.</summary>
        [RuntimeExport("SystemNative_FStat")]
        private static int FStat() => Fail(EBADF);

        // ---------------- environment ----------------

        [RuntimeExport("SystemNative_GetEnviron")]
        private static nint GetEnviron() => 0;

        [RuntimeExport("SystemNative_FreeEnviron")]
        private static void FreeEnviron() { }

        [RuntimeExport("SystemNative_GetPid")]
        private static int GetPid() => 1;

        [RuntimeExport("SystemNative_GetHostName")]
        private static int GetHostName() => Fail(ENOTSUP);

        [RuntimeExport("SystemNative_GetPwNamR")]
        private static int GetPwNamR() => -1;

        [RuntimeExport("SystemNative_GetGroupList")]
        private static int GetGroupList() => Fail(ENOTSUP);

        // ---------------- processes and terminal ----------------

        [RuntimeExport("SystemNative_ForkAndExecProcess")]
        private static int ForkAndExecProcess() => Fail(ENOTSUP);

        [RuntimeExport("SystemNative_Kill")]
        private static int Kill() => Fail(ENOTSUP);

        [RuntimeExport("SystemNative_WaitPidExitedNoHang")]
        private static int WaitPidExitedNoHang() => Fail(ENOTSUP);

        [RuntimeExport("SystemNative_WaitIdAnyExitedNoHangNoWait")]
        private static int WaitIdAnyExitedNoHangNoWait() => Fail(ENOTSUP);

        [RuntimeExport("SystemNative_RegisterForSigChld")]
        private static void RegisterForSigChld() { }

        [RuntimeExport("SystemNative_SetDelayedSigChildConsoleConfigurationHandler")]
        private static void SetDelayedSigChildConsoleConfigurationHandler() { }

        [RuntimeExport("SystemNative_ConfigureTerminalForChildProcess")]
        private static void ConfigureTerminalForChildProcess() { }

        [RuntimeExport("SystemNative_InitializeTerminalAndSignalHandling")]
        private static int InitializeTerminalAndSignalHandling() => 1;
    }
}
