using RemSox.Kernel.Accounts;
using RemSox.Kernel.Configuration;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Security;
using RemSox.Kernel.UI.CLI;

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
/// <param name="OpenerArguments">The arguments a file's association gives its opener before the file (<c>open.ynw = noterminal yesnt</c>).</param>
public sealed record LaunchTarget(LaunchKind Kind, string Name, IReadOnlyList<string> Arguments, ProgramInfo? Program = null, string? Command = null, IReadOnlyList<string>? OpenerArguments = null)
{
    /// <summary>
    /// Gets the arguments the program or opener runs with: for an opened file, the
    /// association's own arguments, then the file's path, then those given after it.
    /// </summary>
    public IReadOnlyList<string> RunArguments => Kind == LaunchKind.File ? [.. OpenerArguments ?? [], Name, .. Arguments] : Arguments;

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
    /// <summary>
    /// Returns what <paramref name="name"/> stands for to <paramref name="account"/> (whose own
    /// programs folder is searched first), or null when it is no program or file that can be opened.
    /// </summary>
    public static LaunchTarget? Resolve(string name, IReadOnlyList<string> args, string workingDirectory, Caller caller)
    {
        if (ProgramRegistry.Find(name) is { } program)
        {
            return new(LaunchKind.Program, program.Name, args, program);
        }

        if (FindFile(name, workingDirectory, caller) is not { } path)
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

        return OpenFile(FileTypes.GetOpener(path, caller.Account)!, path, args);
    }

    /// <summary>The target that opens <paramref name="path"/> with an association's command line, or null when it names no command or program.</summary>
    private static LaunchTarget? OpenFile(string association, string path, IReadOnlyList<string> args)
    {
        (string opener, IReadOnlyList<string> openerArguments) = FileTypes.SplitAssociation(association);
        return ProgramRegistry.Find(opener) is { } program ? new(LaunchKind.File, path, args, program, OpenerArguments: openerArguments)
            : CommandManager.Find(opener) is not null ? new(LaunchKind.File, path, args, Command: opener, OpenerArguments: openerArguments)
            : null;
    }

    /// <summary>
    /// Starts what <paramref name="name"/> stands for from outside a shell (the start menu, a
    /// shortcut, later Explorer). What needs a shell (<see cref="LaunchTarget.NeedsShell"/>)
    /// opens in a new Terminal that runs it as if typed.
    /// </summary>
    public static ProgramStart Start(string name, IReadOnlyList<string> args, string workingDirectory, Caller starter)
    {
        return Resolve(name, args, workingDirectory, starter) is { } target
            ? Start(target, workingDirectory, starter)
            : ProgramStart.Fail($"\"{name}\" is not a program or a file that can be opened.");
    }

    /// <summary>
    /// Opens the file at <paramref name="path"/> with <paramref name="opener"/>, a program or a
    /// command with any arguments before the file, from outside a shell, as an association
    /// to it would (Explorer's Open with).
    /// </summary>
    public static ProgramStart OpenWith(string opener, string path, string workingDirectory, Caller starter)
    {
        return OpenFile(opener, path, []) is { } target
            ? Start(target, workingDirectory, starter)
            : ProgramStart.Fail($"'{FileTypes.SplitAssociation(opener).Opener}' is not a command or program.");
    }

    /// <summary>
    /// Starts a resolved target. What needs a shell opens in a new Terminal that runs it as a
    /// typed line: a file as its opener followed by the file, so the opener chosen is the one used.
    /// </summary>
    private static ProgramStart Start(LaunchTarget target, string workingDirectory, Caller starter)
    {
        if (target.Kind == LaunchKind.Shortcut)
        {
            return Shortcut.StartFile(target.Name, target.Arguments, workingDirectory, starter);
        }

        if (!target.NeedsShell)
        {
            return ProgramRegistry.Start(target.Program!, target.RunArguments, workingDirectory, starter);
        }

        string[] line = target.Kind == LaunchKind.File
            ? [target.Program?.Name ?? target.Command!, .. target.RunArguments]
            : [target.Name, .. target.Arguments];

        return ProgramRegistry.Find(BuiltInPrograms.TerminalName) is { } terminal
            ? ProgramRegistry.Start(terminal, line, workingDirectory, starter)
            : ProgramStart.Fail($"There is no Terminal to run {target.Name} in.");
    }

    /// <summary>
    /// Finds the file a typed name stands for. A name with a folder in it (<c>./hello</c>,
    /// <c>/scripts/hello.ynt</c>) is only looked up there; a bare name is looked up in
    /// <paramref name="workingDirectory"/>, then in each folder of the <c>path</c> setting: the
    /// machine's, then the account's own (by default its programs folder). In
    /// each folder the name is tried as typed when it can be opened (so <c>notes.txt</c> opens),
    /// then with each <c>pathext</c> extension added, in order, so <c>hello</c> finds
    /// <c>hello.rsh</c> or <c>hello.ynt</c> but not <c>hello.txt</c>.
    /// </summary>
    /// <returns>The file's full path, or null when no folder has one.</returns>
    private static string? FindFile(string name, string workingDirectory, Caller caller)
    {
        if (!FileSystemManager.IsMounted)
        {
            return null;
        }

        IEnumerable<string> folders = name.Contains('/')
            ? [workingDirectory]
            : [workingDirectory, .. SystemSettings.GetPathFolders(caller.Account)];
        IReadOnlyList<string> extensions = SystemSettings.GetPathExtensions();

        foreach (string folder in folders)
        {
            try
            {
                string path = Path.GetFullPath(name, folder);
                if (FileTypes.CanOpen(path, caller.Account) && IsRunnable(caller, path))
                {
                    return path;
                }

                foreach (string extension in extensions)
                {
                    if (FileTypes.CanOpen(path + extension, caller.Account) && IsRunnable(caller, path + extension))
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

    /// <summary>Whether <paramref name="path"/> is a file the caller may read, and so run; others are skipped as if missing.</summary>
    private static bool IsRunnable(Caller caller, string path)
    {
        return File.Exists(path)
            && Permissions.Canonicalize(path, "/") is { } canonical
            && Permissions.Can(caller, canonical, Access.Read);
    }
}
