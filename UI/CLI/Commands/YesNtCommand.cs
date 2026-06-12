using RemSox.Processing;

namespace RemSox.UI.CLI.Commands;

public class YesNtCommand : ICommand
{
    public string Name => "yesnt";

    public string Description => "Start the YesNt interpreter";

    private int processId;

    public async Task ExecuteAsync(string? arguments, Action<string> printLine)
    {
        processId = ProcessManager.SpawnProcess<Processes.YesNtInterpreterProcess>(arguments?.Split(',') ?? []);
        await ProcessManager.WaitForProcessExitAsync(processId);
    }

    public Task StopAsync()
    {
        ProcessManager.StopProcess(processId);
        return Task.CompletedTask;
    }
}