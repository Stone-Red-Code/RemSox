using RemSox.Kernel.Processes;
using RemSox.Kernel.Processing;

namespace RemSox.Kernel.UI.CLI.Commands;

public class YesNtCommand : ICommand
{
    public string Name => "yesnt";

    public string Description => "Run a YesNt script (yesnt <file.ynt>)";

    private int processId;

    public async Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        List<string> args = FileCommandHelpers.Split(arguments);

        if (args.Count == 0)
        {
            printLine("Usage: yesnt <file.ynt>");
            return;
        }

        string? scriptPath = ResolveScript(args[0]);

        if (scriptPath is null)
        {
            printLine($"Script not found: {args[0]}");
            return;
        }

        processId = ProcessManager.SpawnProcess<YesNtInterpreterProcess>([scriptPath]);

        if (ProcessManager.GetProcess(processId) is YesNtInterpreterProcess process)
        {
            process.Output = printLine;
        }

        await ProcessManager.WaitForProcessExitAsync(processId);
    }

    public Task StopAsync()
    {
        ProcessManager.StopProcess(processId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the full path of the script, trying the .ynt extension when it was left out.
    /// </summary>
    private static string? ResolveScript(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }

            string withExtension = path + YesNtInterpreterProcess.FileExtension;
            return !path.EndsWith(YesNtInterpreterProcess.FileExtension, StringComparison.OrdinalIgnoreCase) && File.Exists(withExtension)
                ? Path.GetFullPath(withExtension)
                : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return null;
        }
    }
}
