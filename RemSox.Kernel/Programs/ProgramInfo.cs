using RemSox.Kernel.Processing;

namespace RemSox.Kernel.Programs;

/// <summary>
/// A program a user can start by name: from a shell, the start menu or a script. Every start is
/// a new process, made through <see cref="ProgramRegistry.Start"/>.
/// </summary>
/// <remarks>
/// Built-in programs are <see cref="BuiltInProgram{T}"/>s, processes compiled into the kernel.
/// Programs loaded from files (such as WASM modules) would be another kind of program, so the
/// shell, the start menu and everything else that starts programs would not change.
/// </remarks>
public abstract class ProgramInfo(string name)
{
    private readonly string? usage;

    /// <summary>Gets the name the program is started by, like a command's.</summary>
    public string Name { get; } = name;

    /// <summary>Gets a one-line description, shown by <c>help</c>.</summary>
    public required string Description { get; init; }

    /// <summary>Gets the invocation syntax, such as <c>telnet [port]</c>; the name when not set.</summary>
    public string Usage
    {
        get => usage ?? Name;
        init => usage = value;
    }

    /// <summary>Gets the fewest arguments the program accepts.</summary>
    public int MinArguments { get; init; }

    /// <summary>Gets the most arguments the program accepts; none unless set.</summary>
    public int MaxArguments { get; init; }

    /// <summary>
    /// Gets whether a shell that starts the program waits for it to end, shows its output and
    /// stops it on Ctrl+C, like a command. Otherwise it runs beside the shell, as windows and
    /// servers do.
    /// </summary>
    public bool Foreground { get; init; }

    /// <summary>
    /// Gets the check that turns the typed arguments into the process's start arguments (a
    /// default port, a script's full path), or says why the program cannot start. It gets the
    /// starter's working directory to resolve paths against. Without it the arguments pass
    /// through unchanged.
    /// </summary>
    public Func<IReadOnlyList<string>, string, ProgramArguments>? Prepare { get; init; }

    /// <summary>Gets what to tell the user once it started, from its start arguments, or null for nothing.</summary>
    public Func<string[], string>? StartedMessage { get; init; }

    /// <summary>Gets the type of the program's processes, to find the running ones.</summary>
    public abstract Type ProcessType { get; }

    /// <summary>Creates the program's process; only <see cref="ProgramRegistry.Start"/> calls this.</summary>
    internal abstract int Spawn(string[] args, string workingDirectory);
}

/// <summary>A program built into the kernel: each start spawns a new <typeparamref name="T"/>.</summary>
public sealed class BuiltInProgram<T>(string name) : ProgramInfo(name) where T : Process, new()
{
    public override Type ProcessType => typeof(T);

    internal override int Spawn(string[] args, string workingDirectory)
    {
        return ProcessManager.SpawnProcess<T>(args, workingDirectory);
    }
}

/// <summary>The arguments a program starts with, or why it cannot start.</summary>
public readonly record struct ProgramArguments(string[] Args, string? Error)
{
    public static ProgramArguments Ok(params string[] args)
    {
        return new(args, null);
    }

    public static ProgramArguments Fail(string error)
    {
        return new([], error);
    }
}

/// <summary>The outcome of starting a program: its process, or why it did not start.</summary>
public readonly record struct ProgramStart(int ProcessId, string? Message, string? Error)
{
    public static ProgramStart Fail(string error)
    {
        return new(0, null, error);
    }
}
