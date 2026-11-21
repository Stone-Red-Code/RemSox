using RemSox.Kernel.Accounts;
using RemSox.Kernel.Logging;

namespace RemSox.Kernel;

/// <summary>
/// Shutting the machine down and restarting it, for the <c>shutdown</c> and <c>reboot</c>
/// commands and the start menu alike, so both ask the same question first.
/// </summary>
public static class MachinePower
{
    /// <summary>
    /// Returns why <paramref name="caller"/> may not <paramref name="what"/> the machine (to show
    /// the user), or null when they may: anyone logged in at it, as on Windows; from Telnet or
    /// remote desktop only with admin rights, since it ends everyone else's work too.
    /// </summary>
    public static string? Deny(Caller caller, string what)
    {
        // Where the session is now, not how it started: a desktop session started at the machine
        // and taken over by remote desktop is still a Console one.
        string from = caller.Session.Kind == LogonKind.Telnet ? "Telnet" : "remote desktop";
        return caller.Session.IsAtMachine ? null : caller.DenyUnlessAdmin($"{what} the machine from {from}");
    }

    /// <summary>Turns the machine off, after writing the log to disk, which the power-off would otherwise lose.</summary>
    public static void Shutdown()
    {
        Prepare("shutdown");
        Sys.Power.Shutdown();
    }

    /// <summary>Restarts the machine, after writing the log to disk.</summary>
    public static void Reboot()
    {
        Prepare("reboot");
        Sys.Power.Reboot();
    }

    private static void Prepare(string what)
    {
        SystemLog.Log($"System {what} requested.", LogSeverity.Info);
        SystemLog.Flush();
    }
}
