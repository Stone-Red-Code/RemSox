using RemSox.Kernel.Accounts;
using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Programs;

/// <summary>
/// The programs built into the kernel, RemSox's <c>System32</c>, and the one place that starts
/// a program's process (<see cref="Start"/>). Names are resolved by <see cref="Launcher"/>;
/// programs loaded from files would be found there as files, not registered here.
/// </summary>
public static class ProgramRegistry
{
    // Registered at boot, before any shell runs, and only read afterwards.
    private static readonly Dictionary<string, ProgramInfo> programs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<ProgramInfo> registered = [];

    /// <summary>Adds programs; a name already taken by a command or a program is a mistake in the kernel.</summary>
    public static void Register(IEnumerable<ProgramInfo> programsToRegister)
    {
        foreach (ProgramInfo program in programsToRegister)
        {
            if (CommandManager.Find(program.Name) is not null || programs.ContainsKey(program.Name))
            {
                throw new InvalidOperationException($"'{program.Name}' is already a command or program.");
            }

            registered.Add(program);
            programs[program.Name] = program;
        }
    }

    public static ProgramInfo? Find(string name)
    {
        return programs.GetValueOrDefault(name);
    }

    /// <summary>Returns every program, by name.</summary>
    public static IEnumerable<ProgramInfo> GetPrograms()
    {
        return registered.OrderBy(program => program.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Returns the running processes of <paramref name="program"/>.</summary>
    public static IEnumerable<Process> GetProcesses(ProgramInfo program)
    {
        return ProcessManager.GetAllProcesses().Where(process => process.GetType() == program.ProcessType);
    }

    /// <summary>
    /// Starts <paramref name="program"/> in <paramref name="workingDirectory"/> for whoever runs
    /// in <paramref name="starter"/>: checks that they may start it (<see cref="ProgramKind"/>),
    /// checks the argument count, lets the program prepare its arguments, and refuses a second
    /// copy of a program that may only run once. A user program runs in the starter's session,
    /// a service in the system's.
    /// </summary>
    public static ProgramStart Start(ProgramInfo program, IReadOnlyList<string> args, string workingDirectory, LogonSession starter)
    {
        if (starter.HasEnded)
        {
            return ProgramStart.Fail($"Cannot start {program.Name}: the session has ended.");
        }

        LogonSession session;
        switch (program.Kind)
        {
            case ProgramKind.Service when !starter.Account.IsAdmin:
                return ProgramStart.Fail($"Only an administrator can start the {program.Name} service.");

            case ProgramKind.Service:
                session = LogonSessions.System;
                break;

            case ProgramKind.UserProgram when starter.IsSystem:
                return ProgramStart.Fail($"{program.Name} needs a logged-in user; the system only starts services.");

            default:
                session = starter;
                break;
        }

        if (args.Count < program.MinArguments || args.Count > program.MaxArguments)
        {
            return ProgramStart.Fail("Usage: " + program.Usage);
        }

        string[] startArgs = [.. args];
        if (program.Prepare is not null)
        {
            ProgramArguments prepared = program.Prepare(args, workingDirectory);
            if (prepared.Error is not null)
            {
                return ProgramStart.Fail(prepared.Error);
            }

            startArgs = prepared.Args;
        }

        if (ProcessManifest.HasFlag(program.ProcessType, ProcessManifest.ProcessManifestFlags.Singleton) && GetProcesses(program).Any())
        {
            return ProgramStart.Fail($"{program.Name} is already running.");
        }

        if (program.Spawn(startArgs, workingDirectory, session) is not { } processId)
        {
            return ProgramStart.Fail($"{program.Name} failed to start; 'logs' says why.");
        }

        return new(processId, program.StartedMessage?.Invoke(startArgs), null);
    }
}
