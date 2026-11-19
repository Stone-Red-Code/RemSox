using Cosmos.Kernel.System.FileSystem;

using RemSox.Kernel.FileSystem;

namespace RemSox.Kernel.UI.CLI.Commands;

/// <summary>
/// Shared helpers for the file commands.
/// </summary>
internal static class FileCommandHelpers
{
    /// <summary>
    /// Runs a file operation, printing a file system error instead of letting it escape. Without
    /// a disk it only says so: the errors from an empty root ("read-only file system", "no such
    /// directory: /home") would not tell the user why.
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
            DirectoryInfo directory = new(path);

            if (!directory.Exists)
            {
                return context.Fail($"No such directory: {path}");
            }

            foreach (DirectoryInfo subDirectory in directory.GetDirectories().OrderBy(d => d.Name))
            {
                context.Console.WriteLine($"{"<DIR>",8}  {subDirectory.Name}/");
            }

            foreach (FileInfo file in directory.GetFiles().OrderBy(f => f.Name))
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

    public string Description => "Change this shell's current directory (alone: back to /home)";

    public string Usage => "cd [directory]";

    public int MaxArguments => 1;

    public Task<int> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(FileCommandHelpers.Run(context, () =>
        {
            string fullPath = context.ResolvePath(context.Args.Count > 0 ? context.Args[0] : SystemPaths.HomeFolder);

            if (!Directory.Exists(fullPath))
            {
                return context.Fail($"No such directory: {fullPath}");
            }

            context.Session.WorkingDirectory = fullPath;
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
                foreach (string line in File.ReadAllLines(context.ResolvePath(file)))
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
                string path = context.ResolvePath(file);
                if (!File.Exists(path))
                {
                    File.Create(path).Dispose();
                }
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
                File.AppendAllText(path, text);
            }
            else
            {
                File.WriteAllText(path, text);
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
                _ = Directory.CreateDirectory(context.ResolvePath(directory));
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
        bool hasContents = isDirectory && FileCommandHelpers.Run(context, () => Directory.EnumerateFileSystemEntries(path).Any() ? 1 : 0) == 1;

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
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (isDirectory)
            {
                Directory.Delete(path, recursive);
            }
            else
            {
                return context.Fail($"No such file or directory: {path}");
            }

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
            File.Copy(source, destination, overwrite: true);
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
            if (Directory.Exists(source))
            {
                Directory.Move(source, destination);
            }
            else
            {
                File.Move(source, destination, overwrite: true);
            }

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
