using RemSox.Kernel.Logging;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class ShutdownCommand : ICommand
{
    public string Name => "shutdown";

    public string Description => "Shut down the system";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        if (PowerCommands.Deny(context, "shut down") is { } denied)
        {
            return Task.FromResult(denied);
        }

        PowerCommands.Prepare(context, "Shutting down", "shutdown");
        Sys.Power.Shutdown();
        return Task.FromResult(CommandResult.Success);
    }
}

public sealed class RebootCommand : ICommand
{
    public string Name => "reboot";

    public string Description => "Reboot the system";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        if (PowerCommands.Deny(context, "reboot") is { } denied)
        {
            return Task.FromResult(denied);
        }

        PowerCommands.Prepare(context, "Rebooting", "reboot");
        Sys.Power.Reboot();
        return Task.FromResult(CommandResult.Success);
    }
}

internal static class PowerCommands
{
    /// <summary>
    /// Returns the failure to report unless the caller may turn the machine off: anyone logged in at
    /// it, as on Windows; from Telnet or remote desktop only with admin rights, since it ends
    /// everyone else's work too.
    /// </summary>
    public static int? Deny(CommandContext context, string what)
    {
        if (context.Session.Logon.Kind == Accounts.LogonKind.Console)
        {
            return null;
        }

        return context.Caller.DenyUnlessAdmin($"{what} the machine from {context.Session.Logon.Kind}") is { } denied ? context.Fail(denied) : null;
    }

    /// <summary>Says what is about to happen and writes the log to disk, which the power-off would otherwise lose.</summary>
    public static void Prepare(CommandContext context, string doing, string what)
    {
        context.Console.WriteLine(doing + " system...");
        SystemLog.Log($"System {what} requested.", LogSeverity.Info);
        SystemLog.Flush();
    }
}
