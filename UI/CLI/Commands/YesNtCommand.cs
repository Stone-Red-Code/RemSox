using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RemSox.Processing;

namespace RemSox.UI.CLI.Commands
{
    public class YesNtCommand : ICommand
    {
        public string Name => "yesnt";

        public string Description => "Start the YesNt interpreter";

        public async Task ExecuteAsync(string? arguments, Action<string> printLine)
        {
            int processId = ProcessManager.SpawnProcess<Processes.YesNtInterpreterProcess>(arguments?.Split(',') ?? []);
            await ProcessManager.WaitForProcessExitAsync(processId);
        }
    }
}