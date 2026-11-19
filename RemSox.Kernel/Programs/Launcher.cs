using RemSox.Kernel.Configuration;
using RemSox.Kernel.FileSystem;

namespace RemSox.Kernel.Programs;

/// <summary>What kind of thing a name stands for; see <see cref="LaunchTarget"/>.</summary>
public enum LaunchKind
{
    /// <summary>A built-in program, by its name.</summary>
    Program,

    /// <summary>A shell script (<c>.rsh</c>), which a shell runs itself.</summary>
    Script,

    /// <summary>A shortcut (<c>.rsl</c>), which starts its target.</summary>
    Shortcut,

    /// <summary>Any other file, opened by its associated program or command.</summary>
    File,
}

/// <summary>
/// What a name stands for when it is no shell command, and how to start it.
/// </summary>
/// <param name="Kind">What the name stands for.</param>
/// <param name="Name">The program's name or the file's full path: typing it gives this target again.</param>
/// <param name="Arguments">The arguments given after the name.</param>
/// <param name="Program">The program that runs: the one named, or the file's opener when that is a program.</param>
/// <param name="Command">The file's opener when that is a command.</param>
public sealed record LaunchTarget(LaunchKind Kind, string Name, IReadOnlyList<string> Arguments, ProgramInfo? Program = null, string? Command = null)
{
    /// <summary>Gets the arguments the program or opener runs with: an opened file's path comes first.</summary>
    public IReadOnlyList<string> RunArguments => Kind == LaunchKind.File ? [Name, .. Arguments] : Arguments;

    /// <summary>
    /// Gets whether only a shell can run it: a script, a command, or a foreground program (which
    /// shows its output in the shell). From outside a shell these open in a Terminal.
    /// </summary>
    public bool NeedsShell => Kind switch
    {
        LaunchKind.Script => true,
        LaunchKind.Shortcut => false,
        _ => Program is not { Foreground: false },
    };
}

/// <summary>
/// Turns a name into what it stands for, as Windows does once a name is no internal command of
/// the shell: a built-in program (RemSox's <c>System32</c>), else a file found in the current
/// folder or the <c>path</c> folders. The shell (after its own commands) and everything that
/// starts things from outside a shell (the start menu, shortcuts, later Explorer) resolve
/// names here, so they never disagree.
/// </summary>
public static class Launcher
{
    /// <summary>Returns what <paramref name="name"/> stands for, or null when it is no program or file that can be opened.</summary>
    public static LaunchTarget? Resolve(string name, IReadOnlyList<string> args, string workingDirectory)
    {
        if (ProgramRegistry.Find(name) is { } program)
        {
            return new(LaunchKind.Program, program.Name, args, program);
        }

        if (FindFile(name, workingDirectory) is not { } path)
        {
            return null;
        }

        if (FileTypes.IsScript(path))
        {
            return new(LaunchKind.Script, path, args);
        }

        if (FileTypes.IsShortcut(path))
        {
            return new(LaunchKind.Shortcut, path, args);
        }

        string opener = FileTypes.GetOpener(path)!;
        return ProgramRegistry.Find(opener) is { } openerProgram
            ? new(LaunchKind.File, path, args, openerProgram)
            : new(LaunchKind.File, path, args, Command: opener);
    }

    /// <summary>
    /// Starts what <paramref name="name"/> stands for from outside a shell (the start menu, a
    /// shortcut, later Explorer). What needs a shell (<see cref="LaunchTarget.NeedsShell"/>)
    /// opens in a new Terminal that runs it as if typed.
    /// </summary>
    public static ProgramStart Start(string name, IReadOnlyList<string> args, string workingDirectory)
    {
        if (Resolve(name, args, workingDirectory) is not { } target)
        {
            return ProgramStart.Fail($"\"{name}\" is not a program or a file that can be opened.");
        }

        if (target.Kind == LaunchKind.Shortcut)
        {
            return Shortcut.StartFile(target.Name, args, workingDirectory);
        }

        if (!target.NeedsShell)
        {
            return ProgramRegistry.Start(target.Program!, target.RunArguments, workingDirectory);
        }

        return ProgramRegistry.Find(BuiltInPrograms.TerminalName) is { } terminal
            ? ProgramRegistry.Start(terminal, [target.Name, .. args], workingDirectory)
            : ProgramStart.Fail($"There is no Terminal to run {target.Name} in.");
    }

    /// <summary>
    /// Finds the file a typed name stands for. A name with a folder in it (<c>./hello</c>,
    /// <c>/scripts/hello.ynt</c>) is only looked up there; a bare name is looked up in
    /// <paramref name="workingDirectory"/>, then in each folder of the <c>path</c> setting. In
    /// each folder the name is tried as typed when it can be opened (so <c>notes.txt</c> opens),
    /// then with each <c>pathext</c> extension added, in order, so <c>hello</c> finds
    /// <c>hello.rsh</c> or <c>hello.ynt</c> but not <c>hello.txt</c>.
    /// </summary>
    /// <returns>The file's full path, or null when no folder has one.</returns>
    private static string? FindFile(string name, string workingDirectory)
    {
        if (!FileSystemManager.IsMounted)
        {
            return null;
        }

        IEnumerable<string> folders = name.Contains('/') ? [workingDirectory] : [workingDirectory, .. SystemSettings.GetPathFolders()];
        IReadOnlyList<string> extensions = SystemSettings.GetPathExtensions();

        foreach (string folder in folders)
        {
            try
            {
                string path = Path.GetFullPath(name, folder);
                if (FileTypes.CanOpen(path) && File.Exists(path))
                {
                    return path;
                }

                foreach (string extension in extensions)
                {
                    if (FileTypes.CanOpen(path + extension) && File.Exists(path + extension))
                    {
                        return path + extension;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                // A folder that cannot be read (or a name that is no path) just holds nothing.
            }
        }

        return null;
    }
}
