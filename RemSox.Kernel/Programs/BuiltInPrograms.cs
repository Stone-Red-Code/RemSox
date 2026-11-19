using Cosmos.Network.Telnet;

using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Processes;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Programs;

/// <summary>The programs compiled into the kernel, and the runners for script files.</summary>
public static class BuiltInPrograms
{
    /// <summary>Registers the built-in programs and runners; called once at boot, after the commands.</summary>
    public static void Register()
    {
        ProgramRegistry.Register([
            new BuiltInProgram<TerminalProcess>("terminal")
            {
                Description = "Open a GUI Terminal window",
                StartMenuTitle = "Terminal",
            },
            new BuiltInProgram<TestProcess>("test")
            {
                Description = "Open a test window",
                StartMenuTitle = "Test Process",
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
                Prepare = (args, _) => ParsePort(args[0]),
                StartedMessage = args => $"Remote desktop server started on port {args[0]}.",
            },
        ]);

        ProgramRegistry.RegisterRunner(ShellScript.FileExtension, "run");
        ProgramRegistry.RegisterRunner(YesNtInterpreterProcess.FileExtension, "yesnt");
    }

    private static ProgramArguments ParsePort(string text)
    {
        return int.TryParse(text, out int port) && port is > 0 and <= 65535
            ? ProgramArguments.Ok(port.ToString())
            : ProgramArguments.Fail("Not a port: " + text);
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
