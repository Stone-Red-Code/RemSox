using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Programs;

/// <summary>
/// The programs a user can start, and which program runs a file of each type. Everything that
/// starts a program (the shell, the start menu, scripts) goes through <see cref="Start"/>.
/// </summary>
public static class ProgramRegistry
{
    // Registered at boot, before any shell runs, and only read afterwards.
    private static readonly Dictionary<string, ProgramInfo> programs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<ProgramInfo> registered = [];
    private static readonly Dictionary<string, string> runners = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds programs; a name already taken by a command or a program is a mistake in the kernel.</summary>
    public static void Register(IEnumerable<ProgramInfo> programsToRegister)
    {
        foreach (ProgramInfo program in programsToRegister)
        {
            if (CommandManager.Find(program.Name) is not null)
            {
                throw new InvalidOperationException($"'{program.Name}' is already a command or program.");
            }

            registered.Add(program);
            programs[program.Name] = program;
        }
    }

    /// <summary>
    /// Makes files with <paramref name="extension"/> (such as <c>.ynt</c>) run through
    /// <paramref name="runner"/>, a command or program that takes the file as its first
    /// argument: typing <c>hello.ynt</c> then runs <c>yesnt hello.ynt</c>.
    /// </summary>
    public static void RegisterRunner(string extension, string runner)
    {
        runners[extension] = runner;
    }

    /// <summary>Returns the command or program that runs <paramref name="path"/>, or null when nothing runs its type.</summary>
    public static string? FindRunner(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Length > 0 ? runners.GetValueOrDefault(extension) : null;
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
    /// Starts <paramref name="program"/> in <paramref name="workingDirectory"/>: checks the
    /// argument count, lets the program prepare its arguments, and refuses a second copy of a
    /// program that may only run once.
    /// </summary>
    public static ProgramStart Start(ProgramInfo program, IReadOnlyList<string> args, string workingDirectory)
    {
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

        int processId = program.Spawn(startArgs, workingDirectory);
        return new(processId, program.StartedMessage?.Invoke(startArgs), null);
    }
}
