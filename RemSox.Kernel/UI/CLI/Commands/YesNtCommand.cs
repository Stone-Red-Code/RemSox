using RemSox.Kernel.Processes;
using RemSox.Kernel.Processing;

namespace RemSox.Kernel.UI.CLI.Commands;

public sealed class YesNtCommand : ICommand
{
    public string Name => "yesnt";

    public string Description => "Run a YesNt script (typing a .ynt path runs it too)";

    public string Usage => "yesnt <file[.ynt]>";

    public int MinArguments => 1;

    public int MaxArguments => 1;

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        string? scriptPath = ResolveScript(context.ResolvePath(context.Args[0]));

        if (scriptPath is null)
        {
            return context.Fail($"Script not found: {context.Args[0]}");
        }

        int processId = ProcessManager.SpawnProcess<YesNtInterpreterProcess>([scriptPath]);

        if (ProcessManager.GetProcess(processId) is YesNtInterpreterProcess process)
        {
            process.Output = context.Console.WriteLine;
        }

        // The process ID is this run's alone, so Ctrl+C in one shell stops only its own script.
        using (context.Cancellation.Register(() => ProcessManager.StopProcess(processId)))
        {
            await ProcessManager.WaitForProcessExitAsync(processId);
        }

        return context.Cancellation.IsCancellationRequested ? CommandResult.Cancelled : CommandResult.Success;
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
                return path;
            }

            string withExtension = path + YesNtInterpreterProcess.FileExtension;
            return !path.EndsWith(YesNtInterpreterProcess.FileExtension, StringComparison.OrdinalIgnoreCase) && File.Exists(withExtension)
                ? withExtension
                : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return null;
        }
    }
}
