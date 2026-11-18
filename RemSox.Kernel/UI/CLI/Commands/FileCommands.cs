using System.Text;

using Cosmos.Kernel.System.FileSystem;

namespace RemSox.Kernel.UI.CLI.Commands;

/// <summary>
/// Shared helpers for the file commands.
/// </summary>
internal static class FileCommandHelpers
{
    /// <summary>
    /// Splits arguments on whitespace, keeping double-quoted sections together.
    /// </summary>
    public static List<string> Split(string? arguments)
    {
        List<string> result = [];

        if (string.IsNullOrWhiteSpace(arguments))
        {
            return result;
        }

        StringBuilder current = new();
        bool inQuotes = false;
        bool hasToken = false;

        foreach (char c in arguments)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    _ = current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                _ = current.Append(c);
                hasToken = true;
            }
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    /// <summary>
    /// Runs a file operation and prints file system errors instead of letting them escape.
    /// </summary>
    public static Task Run(Action<string> printLine, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            printLine("Error: " + ex.Message);
        }

        return Task.CompletedTask;
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

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        List<string> args = FileCommandHelpers.Split(arguments);
        string path = args.Count > 0 ? args[0] : Directory.GetCurrentDirectory();

        return FileCommandHelpers.Run(printLine, () =>
        {
            DirectoryInfo directory = new(path);

            if (!directory.Exists)
            {
                printLine($"No such directory: {path}");
                return;
            }

            foreach (DirectoryInfo subDirectory in directory.GetDirectories().OrderBy(d => d.Name))
            {
                printLine($"{"<DIR>",8}  {subDirectory.Name}/");
            }

            foreach (FileInfo file in directory.GetFiles().OrderBy(f => f.Name))
            {
                printLine($"{FileCommandHelpers.FormatSize(file.Length),8}  {file.Name}");
            }
        });
    }
}

public sealed class ChangeDirectoryCommand : ICommand
{
    public string Name => "cd";

    public string Description => "Change the current directory";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        List<string> args = FileCommandHelpers.Split(arguments);
        string path = args.Count > 0 ? args[0] : "/";

        return FileCommandHelpers.Run(printLine, () =>
        {
            string fullPath = Path.GetFullPath(path);

            if (!Directory.Exists(fullPath))
            {
                printLine($"No such directory: {path}");
                return;
            }

            Directory.SetCurrentDirectory(fullPath);
        });
    }
}

public sealed class PrintWorkingDirectoryCommand : ICommand
{
    public string Name => "pwd";

    public string Description => "Print the current directory";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        printLine(Directory.GetCurrentDirectory());
        return Task.CompletedTask;
    }
}

public sealed class CatCommand : ICommand
{
    public string Name => "cat";

    public string Description => "Print the contents of a file";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        List<string> args = FileCommandHelpers.Split(arguments);

        if (args.Count == 0)
        {
            printLine("Usage: cat <file>");
            return Task.CompletedTask;
        }

        return FileCommandHelpers.Run(printLine, () =>
        {
            foreach (string line in File.ReadAllLines(args[0]))
            {
                printLine(line);
            }
        });
    }
}

public sealed class TouchCommand : ICommand
{
    public string Name => "touch";

    public string Description => "Create an empty file if it does not exist";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        List<string> args = FileCommandHelpers.Split(arguments);

        if (args.Count == 0)
        {
            printLine("Usage: touch <file>");
            return Task.CompletedTask;
        }

        return FileCommandHelpers.Run(printLine, () =>
        {
            if (!File.Exists(args[0]))
            {
                File.Create(args[0]).Dispose();
            }
        });
    }
}

public sealed class WriteFileCommand : ICommand
{
    public string Name => "write";

    public string Description => "Write text to a file (write [-a] <file> <text>)";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        List<string> args = FileCommandHelpers.Split(arguments);
        bool append = args.Count > 0 && args[0] == "-a";

        if (append)
        {
            args.RemoveAt(0);
        }

        if (args.Count < 2)
        {
            printLine("Usage: write [-a] <file> <text>");
            return Task.CompletedTask;
        }

        string text = string.Join(' ', args.Skip(1)) + Environment.NewLine;

        return FileCommandHelpers.Run(printLine, () =>
        {
            if (append)
            {
                File.AppendAllText(args[0], text);
            }
            else
            {
                File.WriteAllText(args[0], text);
            }
        });
    }
}

public sealed class MakeDirectoryCommand : ICommand
{
    public string Name => "mkdir";

    public string Description => "Create a directory";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        List<string> args = FileCommandHelpers.Split(arguments);

        if (args.Count == 0)
        {
            printLine("Usage: mkdir <directory>");
            return Task.CompletedTask;
        }

        return FileCommandHelpers.Run(printLine, () => Directory.CreateDirectory(args[0]));
    }
}

public sealed class RemoveCommand : ICommand
{
    public string Name => "rm";

    public string Description => "Remove a file, or a directory with -r";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        List<string> args = FileCommandHelpers.Split(arguments);
        bool recursive = args.Remove("-r");

        if (args.Count == 0)
        {
            printLine("Usage: rm [-r] <path>");
            return Task.CompletedTask;
        }

        return FileCommandHelpers.Run(printLine, () =>
        {
            string path = args[0];

            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                if (!recursive && Directory.EnumerateFileSystemEntries(path).Any())
                {
                    printLine($"{path} is not empty, use rm -r");
                    return;
                }

                Directory.Delete(path, recursive);
            }
            else
            {
                printLine($"No such file or directory: {path}");
            }
        });
    }
}

public sealed class CopyCommand : ICommand
{
    public string Name => "cp";

    public string Description => "Copy a file";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        List<string> args = FileCommandHelpers.Split(arguments);

        if (args.Count < 2)
        {
            printLine("Usage: cp <source> <destination>");
            return Task.CompletedTask;
        }

        return FileCommandHelpers.Run(printLine, () => File.Copy(args[0], ResolveDestination(args[0], args[1]), overwrite: true));
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

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        List<string> args = FileCommandHelpers.Split(arguments);

        if (args.Count < 2)
        {
            printLine("Usage: mv <source> <destination>");
            return Task.CompletedTask;
        }

        return FileCommandHelpers.Run(printLine, () =>
        {
            string destination = CopyCommand.ResolveDestination(args[0], args[1]);

            if (Directory.Exists(args[0]))
            {
                Directory.Move(args[0], destination);
            }
            else
            {
                File.Move(args[0], destination, overwrite: true);
            }
        });
    }
}

public sealed class DiskFreeCommand : ICommand
{
    public string Name => "df";

    public string Description => "Show mounted file systems and free space";

    public Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        if (VfsManager.Mounts.Count == 0)
        {
            printLine("No file systems mounted.");
            return Task.CompletedTask;
        }

        foreach (VfsMount mount in VfsManager.Mounts)
        {
            if (VfsManager.TryStatFs(mount.MountPoint, out VfsStatFs stats))
            {
                long total = (long)(stats.Blocks * stats.BlockSize);
                long free = (long)(stats.AvailableBlocks * stats.BlockSize);
                printLine($"{mount.MountPoint} ({mount.Name}): {FileCommandHelpers.FormatSize(free)} free of {FileCommandHelpers.FormatSize(total)}");
            }
            else
            {
                printLine($"{mount.MountPoint} ({mount.Name})");
            }
        }

        return Task.CompletedTask;
    }
}
