using System.Globalization;

using RemSox.Kernel.Processing;
using RemSox.Kernel.Programs;

namespace RemSox.Kernel.UI.CLI.Commands;

/// <summary>
/// Shows and manages the services (servers such as Telnet and remote desktop): which run, which
/// start at boot (<see cref="ServiceList"/>), and starting or stopping them. Services run as the
/// system, so only administrators may change them.
/// </summary>
public sealed class ServiceCommand : ICommand
{
    public string Name => "service";

    public string Description => "Show the services, start and stop them, and choose which start at boot";

    public string Usage => "service [status | enable <name> [arguments...] | disable <name> | start <name> [arguments...] | stop <name>]";

    public Task<int> ExecuteAsync(CommandContext context)
    {
        IReadOnlyList<string> args = context.Args;
        string action = args.Count > 0 ? args[0].ToLowerInvariant() : "status";

        if (action == "status" && args.Count <= 1)
        {
            return Task.FromResult(Status(context));
        }

        if (action is not ("enable" or "disable" or "start" or "stop") || args.Count < 2)
        {
            return Task.FromResult(context.UsageError());
        }

        if (!context.Session.Account.IsAdmin)
        {
            return Task.FromResult(context.Fail("Only an administrator can change services."));
        }

        if (ProgramRegistry.Find(args[1]) is not { Kind: ProgramKind.Service } service)
        {
            return Task.FromResult(context.Fail($"{args[1]} is no service (see 'service status')."));
        }

        string[] arguments = [.. args.Skip(2)];
        if (action is "disable" or "stop" && arguments.Length > 0)
        {
            return Task.FromResult(context.UsageError());
        }

        return Task.FromResult(action switch
        {
            "enable" => Enable(context, service, arguments),
            "disable" => Disable(context, service),
            "start" => Start(context, service, arguments.Length > 0 ? arguments : ServiceList.GetArguments(service.Name) ?? []),
            _ => Stop(context, service),
        });
    }

    private static int Status(CommandContext context)
    {
        foreach (ProgramInfo service in ProgramRegistry.GetPrograms().Where(p => p.Kind == ProgramKind.Service))
        {
            List<Process> running = [.. ProgramRegistry.GetProcesses(service)];
            string state = running.Count > 0
                ? "running (process " + string.Join(", ", running.Select(p => p.Id.ToString(CultureInfo.InvariantCulture))) + ")"
                : "stopped";
            string boot = ServiceList.GetArguments(service.Name) is { } arguments
                ? "starts at boot" + (arguments.Length > 0 ? ": " + string.Join(' ', [service.Name, .. arguments.Select(CommandLine.Quote)]) : string.Empty)
                : "not at boot";

            context.Console.WriteLine($"{service.Name,-8} {state,-22} {boot}");
        }

        return CommandResult.Success;
    }

    /// <summary>Makes the service start at boot, and starts it now unless it runs already.</summary>
    private static int Enable(CommandContext context, ProgramInfo service, string[] arguments)
    {
        if (ServiceList.Enable(service.Name, arguments) is { } error)
        {
            return context.Fail(error);
        }

        context.Console.WriteLine($"{service.Name} starts at boot.");
        return ProgramRegistry.GetProcesses(service).Any() ? CommandResult.Success : Start(context, service, arguments);
    }

    /// <summary>Stops the service from starting at boot, and stops it now.</summary>
    private static int Disable(CommandContext context, ProgramInfo service)
    {
        if (ServiceList.Disable(service.Name) is { } error)
        {
            return context.Fail(error);
        }

        context.Console.WriteLine($"{service.Name} no longer starts at boot.");
        return ProgramRegistry.GetProcesses(service).Any() ? Stop(context, service) : CommandResult.Success;
    }

    private static int Start(CommandContext context, ProgramInfo service, string[] arguments)
    {
        return ProgramRunner.Report(ProgramRegistry.Start(service, arguments, context.Session.WorkingDirectory, context.Session.Logon), context.Console);
    }

    private static int Stop(CommandContext context, ProgramInfo service)
    {
        List<Process> running = [.. ProgramRegistry.GetProcesses(service)];
        if (running.Count == 0)
        {
            return context.Fail($"{service.Name} is not running.");
        }

        foreach (Process process in running)
        {
            ProcessManager.StopProcess(process.Id);
        }

        context.Console.WriteLine($"Stopped {service.Name}.");
        return CommandResult.Success;
    }
}
