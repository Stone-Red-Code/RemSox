using Cosmos.Kernel.System.FileSystem;

using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Security;

namespace RemSox.Kernel.UI.CLI.Commands;

/// <summary>
/// Shared helpers for the file commands.
/// </summary>
internal static class FileCommandHelpers
{
    /// <summary>
    /// Runs a file operation, printing a file system error (a refused permission included) instead
    /// of letting it escape. File commands go through <see cref="UserFiles"/>, so every path is
    /// checked for the shell's caller. Without
    /// a disk it only says so: the errors from an empty root ("read-only file system", "no such
    /// directory: /users") would not tell the user why.
    /// </summary>
    public static int Run(CommandContext context, Func<int> action)
    {
        if (RequireDisk(context) is { } noDisk)
        {
            return noDisk;
        }

        try
        {
            return action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return context.Fail("Error: " + ex.Message);
        }
    }

    /// <summary>
    /// Reports that no disk is mounted and returns the failure code, or returns null when one is,
    /// for commands that use files without going through <see cref="Run"/>.
    /// </summary>
    public static int? RequireDisk(CommandContext context)
    {
        return FileSystemManager.IsMounted ? null : context.Fail(FileSystemManager.NoDiskMessage);
    }

    /// <summary>Removes <paramref name="flag"/> from the arguments, reporting whether it was there.</summary>
    public static bool TakeFlag(List<string> args, string flag)
    {
        return args.Remove(flag);
    }

    public static string FormatSize(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.0}M",
            >= 1024 => $"{bytes / 1024.0:0.0}K",
            _ => bytes + "B"
        };
    }
}

public sealed class ListDirectoryCommand : ICommand
{
    public string Name => "ls";

    public string Description => "List the contents of a directory";

    public string Usage => "ls [directory]";

    public int MaxArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        string path = context.Args.Count > 0 ? context.ResolvePath(context.Args[0]) : context.Session.WorkingDirectory;

        return Task.FromResult(FileCommandHelpers.Run(context, () =>
        {
            (DirectoryInfo[] folders, FileInfo[] files) = UserFiles.List(context.Caller, path);

            foreach (DirectoryInfo subDirectory in folders.OrderBy(d => d.Name))
            {
                context.Console.WriteLine($"{"<DIR>",8}  {subDirectory.Name}/");
            }

            foreach (FileInfo file in files.OrderBy(f => f.Name))
            {
                context.Console.WriteLine($"{FileCommandHelpers.FormatSize(file.Length),8}  {file.Name}");
            }

            return CommandResult.Success;
        }));
    }
}

public sealed class ChangeDirectoryCommand : ICommand
{
    public string Name => "cd";

    public string Description => "Change this shell's current directory (alone: back to your home folder)";

    public string Usage => "cd [directory]";

    public int MaxArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(FileCommandHelpers.Run(context, () =>
        {
            string fullPath = context.ResolvePath(context.Args.Count > 0 ? context.Args[0] : context.Session.Account.HomeFolder);
            string folder = UserFiles.Authorize(context.Caller, fullPath, Access.Read);

            if (!Directory.Exists(folder))
            {
                return context.Fail($"No such directory: {fullPath}");
            }

            context.Session.WorkingDirectory = folder;
            return CommandResult.Success;
        }));
    }
}

public sealed class PrintWorkingDirectoryCommand : ICommand
{
    public string Name => "pwd";

    public string Description => "Print this shell's current directory";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        context.Console.WriteLine(context.Session.WorkingDirectory);
        return Task.FromResult(CommandResult.Success);
    }
}

public sealed class CatCommand : ICommand
{
    public string Name => "cat";

    public string Description => "Print the contents of files";

    public string Usage => "cat <file>...";

    public int MinArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(FileCommandHelpers.Run(context, () =>
        {
            foreach (string file in context.Args)
            {
                foreach (string line in UserFiles.ReadAllLines(context.Caller, context.ResolvePath(file)))
                {
                    if (context.Cancellation.IsCancellationRequested)
                    {
                        return CommandResult.Cancelled;
                    }

                    context.Console.WriteLine(line);
                }
            }

            return CommandResult.Success;
        }));
    }
}

public sealed class TouchCommand : ICommand
{
    public string Name => "touch";

    public string Description => "Create empty files that do not exist yet";

    public string Usage => "touch <file>...";

    public int MinArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(FileCommandHelpers.Run(context, () =>
        {
            foreach (string file in context.Args)
            {
                UserFiles.CreateFile(context.Caller, context.ResolvePath(file));
            }

            return CommandResult.Success;
        }));
    }
}

public sealed class WriteFileCommand : ICommand
{
    public string Name => "write";

    public string Description => "Write text to a file, replacing it or appending with -a";

    public string Usage => "write [-a] <file> <text>...";

    public int MinArguments => 2;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        List<string> args = [.. context.Args];
        bool append = args.Count > 0 && args[0] == "-a";

        if (append)
        {
            args.RemoveAt(0);
        }

        if (args.Count < 2)
        {
            return Task.FromResult(context.UsageError());
        }

        string path = context.ResolvePath(args[0]);
        string text = string.Join(' ', args.Skip(1)) + Environment.NewLine;

        return Task.FromResult(FileCommandHelpers.Run(context, () =>
        {
            if (append)
            {
                UserFiles.AppendAllText(context.Caller, path, text);
            }
            else
            {
                UserFiles.WriteAllText(context.Caller, path, text);
            }

            return CommandResult.Success;
        }));
    }
}

public sealed class MakeDirectoryCommand : ICommand
{
    public string Name => "mkdir";

    public string Description => "Create directories, with any missing parents";

    public string Usage => "mkdir <directory>...";

    public int MinArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(FileCommandHelpers.Run(context, () =>
        {
            foreach (string directory in context.Args)
            {
                UserFiles.CreateDirectory(context.Caller, context.ResolvePath(directory));
            }

            return CommandResult.Success;
        }));
    }
}

public sealed class RemoveCommand : ICommand
{
    public string Name => "rm";

    public string Description => "Remove a file, or a directory with -r (asks first unless -f)";

    public string Usage => "rm [-r] [-f] <path>";

    public int MinArguments => 1;

    public int MaxArguments => 3;

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        List<string> args = [.. context.Args];
        bool recursive = FileCommandHelpers.TakeFlag(args, "-r");
        bool force = FileCommandHelpers.TakeFlag(args, "-f");

        if (args.Count != 1)
        {
            return context.UsageError();
        }

        string path = context.ResolvePath(args[0]);
        bool isDirectory = Directory.Exists(path);
        bool hasContents = isDirectory && FileCommandHelpers.Run(context, () => UserFiles.HasEntries(context.Caller, path) ? 1 : 0) == 1;

        if (hasContents && !recursive)
        {
            return context.Fail($"{path} is not empty, use rm -r");
        }

        if (hasContents && !force && !await context.ConfirmAsync($"Delete {path} and everything in it?"))
        {
            context.Console.WriteLine("Not deleted.");
            return CommandResult.Failure;
        }

        return FileCommandHelpers.Run(context, () =>
        {
            UserFiles.Delete(context.Caller, path, recursive);
            return CommandResult.Success;
        });
    }
}

public sealed class CopyCommand : ICommand
{
    public string Name => "cp";

    public string Description => "Copy a file";

    public string Usage => "cp <source> <destination>";

    public int MinArguments => 2;

    public int MaxArguments => 2;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        string source = context.ResolvePath(context.Args[0]);
        string destination = ResolveDestination(source, context.ResolvePath(context.Args[1]));

        return Task.FromResult(FileCommandHelpers.Run(context, () =>
        {
            UserFiles.CopyFile(context.Caller, source, destination, overwrite: true);
            return CommandResult.Success;
        }));
    }

    internal static string ResolveDestination(string source, string destination)
    {
        return Directory.Exists(destination) ? Path.Combine(destination, Path.GetFileName(source)) : destination;
    }
}

public sealed class MoveCommand : ICommand
{
    public string Name => "mv";

    public string Description => "Move or rename a file or directory";

    public string Usage => "mv <source> <destination>";

    public int MinArguments => 2;

    public int MaxArguments => 2;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        string source = context.ResolvePath(context.Args[0]);
        string destination = CopyCommand.ResolveDestination(source, context.ResolvePath(context.Args[1]));

        return Task.FromResult(FileCommandHelpers.Run(context, () =>
        {
            UserFiles.Move(context.Caller, source, destination, overwrite: true);
            return CommandResult.Success;
        }));
    }
}

public sealed class DiskFreeCommand : ICommand
{
    public string Name => "df";

    public string Description => "Show mounted file systems and free space";

    public int MaxArguments => 0;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        if (VfsManager.Mounts.Count == 0)
        {
            return Task.FromResult(context.Fail("No file systems mounted."));
        }

        foreach (VfsMount mount in VfsManager.Mounts)
        {
            if (VfsManager.TryStatFs(mount.MountPoint, out VfsStatFs stats))
            {
                long total = (long)(stats.Blocks * stats.BlockSize);
                long free = (long)(stats.AvailableBlocks * stats.BlockSize);
                context.Console.WriteLine($"{mount.MountPoint} ({mount.Name}): {FileCommandHelpers.FormatSize(free)} free of {FileCommandHelpers.FormatSize(total)}");
            }
            else
            {
                context.Console.WriteLine($"{mount.MountPoint} ({mount.Name})");
            }
        }

        return Task.FromResult(CommandResult.Success);
    }
}
