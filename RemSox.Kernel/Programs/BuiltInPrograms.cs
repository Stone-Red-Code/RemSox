using Cosmos.Network.Telnet;

using RemSox.Kernel.Configuration;
using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Processes;
using RemSox.Shared.Cryptography;

namespace RemSox.Kernel.Programs;

/// <summary>The programs compiled into the kernel.</summary>
public static class BuiltInPrograms
{
    /// <summary>The Terminal program, which also runs files that need a shell when started from outside one.</summary>
    public const string TerminalName = "terminal";

    /// <summary>
    /// The program that runs a line in a shell without a Terminal window: put in front of
    /// anything (in a shortcut, a file association) that should never open one.
    /// </summary>
    public const string NoTerminalName = "noterminal";

    /// <summary>Registers the built-in programs; called once at boot, after the commands.</summary>
    public static void Register()
    {
        ProgramRegistry.Register([
            new BuiltInProgram<TerminalProcess>(TerminalName)
            {
                Description = "Open a GUI Terminal window; given a command, run it and close unless it fails",
                Usage = "terminal [command or script [arguments...]]",
                MaxArguments = int.MaxValue,
            },
            new BuiltInProgram<NoTerminalProcess>(NoTerminalName)
            {
                Description = "Run a command or script without a Terminal window; its output goes to its log",
                Usage = "noterminal <command or script> [arguments...]",
                MinArguments = 1,
                MaxArguments = int.MaxValue,
            },
            new BuiltInProgram<ExplorerProcess>("explorer")
            {
                Description = "Browse folders and open files in a window",
                Usage = "explorer [folder]",
                MaxArguments = 1,
                Prepare = (args, workingDirectory) => args.Count == 0 ? ProgramArguments.Ok() : FindFolder(args[0], workingDirectory),
            },
            new BuiltInProgram<TestProcess>("test")
            {
                Description = "Open a test window",
            },
            new BuiltInProgram<YesNtInterpreterProcess>("yesnt")
            {
                Description = "Run a YesNt script (typing a .ynt path runs it too)",
                Usage = "yesnt <file[.ynt]>",
                MinArguments = 1,
                MaxArguments = 1,
                Foreground = true,
                Prepare = (args, workingDirectory) => FindScript(args[0], workingDirectory),
            },
            new BuiltInProgram<DesktopProcess>("desktop")
            {
                Description = "Start the graphical desktop",
                StartedMessage = _ => "Starting the desktop...",
            },
            new BuiltInProgram<TelnetProcess>("telnet")
            {
                Description = "Serve this shell over Telnet, unencrypted and without login",
                Usage = "telnet [port 1-65535, default 23]",
                MaxArguments = 1,
                Prepare = (args, _) => args.Count == 0 ? ProgramArguments.Ok(TelnetServer.DefaultPort.ToString()) : ParsePort(args[0]),
                StartedMessage = args => $"Telnet server started on port {args[0]}. Anyone who can reach it gets a shell.",
            },
            new BuiltInProgram<RemoteDesktopProcess>("rdp")
            {
                Description = "Start the remote desktop server",
                Usage = "rdp <port 1-65535>",
                MinArguments = 1,
                MaxArguments = 1,
                Prepare = (args, _) => PasswordHash.Parse(SystemSettings.Get(SystemSettings.RdpPasswordKey)) is null
                    ? ProgramArguments.Fail($"Set a password for viewers first: config set {SystemSettings.RdpPasswordKey} <password>")
                    : ParsePort(args[0]),
                StartedMessage = args => $"Remote desktop server started on port {args[0]}. Viewers need the {SystemSettings.RdpPasswordKey} password.",
            },
        ]);
    }

    private static ProgramArguments ParsePort(string text)
    {
        return int.TryParse(text, out int port) && port is > 0 and <= 65535
            ? ProgramArguments.Ok(port.ToString())
            : ProgramArguments.Fail("Not a port: " + text);
    }

    /// <summary>Returns the folder's full path, or why it cannot be shown.</summary>
    private static ProgramArguments FindFolder(string name, string workingDirectory)
    {
        if (!FileSystemManager.IsMounted)
        {
            return ProgramArguments.Fail(FileSystemManager.NoDiskMessage);
        }

        try
        {
            string path = Path.GetFullPath(name, workingDirectory);
            if (Directory.Exists(path))
            {
                return ProgramArguments.Ok(path);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // An unusable path is reported like a missing one.
        }

        return ProgramArguments.Fail("No such folder: " + name);
    }

    /// <summary>Returns the script's full path, trying the .ynt extension when it was left out.</summary>
    private static ProgramArguments FindScript(string name, string workingDirectory)
    {
        if (!FileSystemManager.IsMounted)
        {
            return ProgramArguments.Fail(FileSystemManager.NoDiskMessage);
        }

        try
        {
            string path = Path.GetFullPath(name, workingDirectory);
            if (File.Exists(path))
            {
                return ProgramArguments.Ok(path);
            }

            string withExtension = path + YesNtInterpreterProcess.FileExtension;
            if (!path.EndsWith(YesNtInterpreterProcess.FileExtension, StringComparison.OrdinalIgnoreCase) && File.Exists(withExtension))
            {
                return ProgramArguments.Ok(withExtension);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // An unusable path is reported like a missing one.
        }

        return ProgramArguments.Fail("Script not found: " + name);
    }
}
