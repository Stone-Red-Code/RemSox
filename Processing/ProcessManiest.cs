using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RemSox.Processes;
using RemSox.Processing;

namespace RemSox.Processing
{
    public static class ProcessManifest
    {
        [Flags]
        public enum ProcessManifestFlags
        {
            None = 0,
            Singleton = 1 << 0,
            System = 1 << 1
        }

        private static readonly Dictionary<Type, ProcessManifestFlags> map = new()
        {
            { typeof(DesktopProcess), ProcessManifestFlags.Singleton | ProcessManifestFlags.System },
            { typeof(CliProcess), ProcessManifestFlags.Singleton | ProcessManifestFlags.System }
        };

        public static bool HasFlag<T>(ProcessManifestFlags flag) where T : Process
        {
            return map.TryGetValue(typeof(T), out var flags) && flags.HasFlag(flag);
        }

        public static bool HasFlag(Type t, ProcessManifestFlags flag)
        {
            return map.TryGetValue(t, out var flags) && flags.HasFlag(flag);
        }
    }
}