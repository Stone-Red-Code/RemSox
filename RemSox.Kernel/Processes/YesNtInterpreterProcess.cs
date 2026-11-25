using RemSox.Kernel.Logging;
using RemSox.Kernel.Processing;
using RemSox.Kernel.Security;

using YesNt.Interpreter.Enums;
using YesNt.Interpreter.Runtime;

namespace RemSox.Kernel.Processes;

/// <summary>
/// Runs a YesNt script (.ynt) file. The script path is the first start argument.
/// </summary>
/// <remarks>
/// The process steps the script a few lines each tick. Whatever the script waits for (a
/// <c>sleep</c>, a line the user has not entered yet, <c>ui_wait</c>) makes the interpreter
/// return at once, and the process lets the main loop idle until it next ticks. Tasks the
/// script starts take turns with it on the main loop (<see cref="TaskMode.Cooperative"/>), so
/// they may use windows like the script itself. Its console and files are a
/// <see cref="YesNtScriptHost"/>: the terminal running it and the files its caller may read.
/// </remarks>
public sealed class YesNtInterpreterProcess() : Process("YesNtInterpreter"), IConsoleOutput, IConsoleInput
{
    public const string FileExtension = ".ynt";

    private const int LinesPerTick = 50;

    private YesNtInterpreter interpreter = null!;
    private YesNtScriptHost host = null!;
    private YesNtWindowStatements windowStatements = null!;

    private string? scriptPath;
    private bool prepared;

    /// <inheritdoc/>
    public Action<string>? Output { get; set; }

    /// <inheritdoc/>
    public Action? Clear { get; set; }

    /// <inheritdoc/>
    public Func<string, CancellationToken, Task<string?>>? ReadLine { get; set; }

    internal override void Start(string[] args)
    {
        host = new YesNtScriptHost(this);
        interpreter = new YesNtInterpreter { Host = host, TaskMode = TaskMode.Cooperative };
        windowStatements = new YesNtWindowStatements(this);
        windowStatements.Register(interpreter);
        RegisterWaitStatement();

        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            Logger.Log("No script file given.", LogSeverity.Error);
            RequestStop();
            return;
        }

        scriptPath = Path.GetFullPath(args[0], WorkingDirectory);
        Name = "YesNt: " + Path.GetFileName(scriptPath);
    }

    internal override void Tick()
    {
        if (!prepared)
        {
            prepared = true;

            if (!TryPrepare())
            {
                RequestStop();
                return;
            }
        }

        StepResult result = interpreter.Step(LinesPerTick);

        if (!interpreter.IsRunning)
        {
            RequestStop();
        }
        else if (result != StepResult.Waiting)
        {
            // More to run: the next iteration at once. A waiting script lets the loop idle.
            MainLoop.StayAwake();
        }
    }

    internal override void Stop()
    {
        interpreter.Stop();
        host.Close();
    }

    /// <summary>
    /// Writes a line of the script's output: to the terminal running it, or to its log.
    /// </summary>
    internal void PrintLine(string line)
    {
        if (Output is not null)
        {
            Output(line);
        }
        else
        {
            Logger.Log(line, LogSeverity.Info);
        }
    }

    // ui_wait [milliseconds]: the script goes on once the user does something in its windows
    // (YesNtWindowStatements.EventCount), or the time has passed.
    private void RegisterWaitStatement()
    {
        interpreter.AddStatement(
            new StatementInformation("ui_wait", SearchMode.StartOfLine, SpaceAround.None),
            (args, context) =>
            {
                string time = args.Trim();
                int milliseconds = 0;
                if (time.Length > 0 && (!int.TryParse(time, out milliseconds) || milliseconds < 0))
                {
                    context.Exit($"[ui_wait] Not a number of milliseconds: {time}", true);
                    return;
                }

                int events = windowStatements.EventCount;
                long until = milliseconds > 0 ? Environment.TickCount64 + milliseconds : long.MaxValue;
                context.WaitUntil(() => windowStatements.EventCount != events || Environment.TickCount64 >= until);
            });
    }

    private bool TryPrepare()
    {
        if (scriptPath is null || !UserFiles.Exists(Caller, scriptPath))
        {
            PrintLine($"Script not found: {scriptPath}");
            return false;
        }

        try
        {
            // The host reads the script, and everything it imports, as the caller.
            interpreter.Prepare(scriptPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PrintLine($"Could not load {scriptPath}: {ex.Message}");
            return false;
        }
    }
}
