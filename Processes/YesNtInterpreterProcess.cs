using System;
using RemSox.Processing;
using RemSox.Utils;
using YesNt.Interpreter.Runtime;
using YesNt.Interpreter.Utilities;

namespace RemSox.Processes
{
    public class YesNtInterpreterProcess() : Process("YesNtInterpreter")
    {
        YesNtInterpreter interpreter = null!;

        internal override void Start(string[] args)
        {
            // TODO: eventually we want to load these from a file instead of hardcoding them here
            args = [
                "win_create \"Test\" 320 240",
                "global winId = %win_last_id",
                "win_flush ${winId}",
                "print ${winId}",
                "label test:",
                "goto test"
            ];
            interpreter = new();

            YesNtWindowStatements.Register(interpreter, this);

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