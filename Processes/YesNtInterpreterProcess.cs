using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RemSox.Processing;
using YesNt.Interpreter.Runtime;
using YesNt.Interpreter.Utilities;

namespace RemSox.Processes
{
    public class YesNtInterpreterProcess() : Process("YesNtInterpreter")
    {
        YesNtInterpreter interpreter = null!;

        internal override void Start(string[] args)
        {
            interpreter = new();
            interpreter.AddStatement(new StatementInformation("%test", YesNt.Interpreter.Enums.SearchMode.Contains, YesNt.Interpreter.Enums.SpaceAround.None) { KeepStatementInArgs = true, Priority = YesNt.Interpreter.Enums.Priority.PreProcessing }, (args, context) =>
            {
                context.CurrentLine = TemplateProcessor.ProcessSimplePlaceholders(args, "%test", "Test successful!");
            });
            interpreter.Prepare([.. args]);
        }

        internal override void Tick()
        {
            if (interpreter.IsRunning)
            {
                interpreter.Step();
            }
            else
            {
                RequestStop();
            }
        }

        internal override void Stop()
        {
            interpreter.Stop();
        }
    }
}