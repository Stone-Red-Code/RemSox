using System.Text;

using RemSox.Kernel.Logging;
using RemSox.Kernel.Processing;
using RemSox.Kernel.Security;
using RemSox.Kernel.Utils;

using YesNt.Interpreter.Enums;
using YesNt.Interpreter.Runtime;
using YesNt.Interpreter.Utilities;

namespace RemSox.Kernel.Processes;

/// <summary>
/// Runs a YesNt script (.ynt) file. The script path is the first start argument.
/// </summary>
/// <remarks>
/// The interpreter's own <c>sleep</c>, <c>%read_line</c> and <c>%read_key</c> wait inside
/// <see cref="YesNtInterpreter.Step"/>, which would stop the whole system (the main loop) while
/// they wait, and read the machine's console rather than the script's terminal. They are
/// replaced: <c>sleep</c> notes when to go on, and the two input statements take lines read
/// through the shell running the script (<see cref="IConsoleInput"/>). The process steps a line
/// at a time and stops stepping, letting the main loop idle, while the script waits.
/// </remarks>
public sealed class YesNtInterpreterProcess() : Process("YesNtInterpreter"), IConsoleOutput, IConsoleInput
{
    public const string FileExtension = ".ynt";

    private const int LinesPerTick = 50;
    private const string ReadLineToken = "%read_line";
    private const string ReadKeyToken = "%read_key";

    private readonly StringBuilder pendingOutput = new();

    private YesNtInterpreter interpreter = null!;

    private string? scriptPath;

    private bool prepared;

    // When a sleep ends (Environment.TickCount64), the line being read for the script, and the
    // lines read for its current line, one for each %read_line or %read_key in it.
    private long sleepUntil;
    private Task<string?>? pendingRead;
    private readonly List<string> readLines = [];
    private readonly CancellationTokenSource readCancellation = new();

    /// <inheritdoc/>
    public Action<string>? Output { get; set; }

    /// <inheritdoc/>
    public Func<string, CancellationToken, Task<string?>>? ReadLine { get; set; }

    // The script's windows, and while it waits for something to happen in them (ui_wait), the
    // event count it waits to change.
    private YesNtWindowStatements windowStatements = null!;
    private int? waitingForEvent;

    private bool IsWaiting => pendingRead is not null || Environment.TickCount64 < sleepUntil || IsWaitingForEvent;

    // An event ends the wait, and so does its time running out (a wait with no time has none).
    private bool IsWaitingForEvent
    {
        get
        {
            if (waitingForEvent is { } count && count == windowStatements.EventCount && (eventWaitUntil == 0 || Environment.TickCount64 < eventWaitUntil))
            {
                return true;
            }

            waitingForEvent = null;
            return false;
        }
    }

    private long eventWaitUntil;

    internal override void Start(string[] args)
    {
        interpreter = new();
        windowStatements = new YesNtWindowStatements(this);
        windowStatements.Register(interpreter);
        RegisterWaitStatements();
        interpreter.OnDebugOutput += WriteOutput;

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

        if (!interpreter.IsRunning)
        {
            RequestStop();
            return;
        }

        if (!TakeReadLine())
        {
            return;
        }

        for (int line = 0; line < LinesPerTick && interpreter.IsRunning && !IsWaiting; line++)
        {
            _ = interpreter.Step(1);
        }

        // More to run: the next iteration at once. A waiting script lets the loop idle.
        if (interpreter.IsRunning && !IsWaiting)
        {
            MainLoop.StayAwake();
        }
    }

    internal override void Stop()
    {
        readCancellation.Cancel();
        interpreter.Stop();
        FlushOutput();
    }

    // Hands the line read for the script over once the user entered it. False while it is
    // still being read, or once the terminal closed, which ends the script.
    private bool TakeReadLine()
    {
        if (pendingRead is null)
        {
            return true;
        }

        if (!pendingRead.IsCompleted)
        {
            return false;
        }

        string? line = pendingRead.IsCompletedSuccessfully ? pendingRead.Result : null;
        pendingRead = null;
        if (line is null)
        {
            PrintLine("No more input: the script ends.");
            interpreter.Stop();
            RequestStop();
            return false;
        }

        readLines.Add(line);
        return true;
    }

    private void RegisterWaitStatements()
    {
        interpreter.RemoveStatement("sleep");
        interpreter.RemoveStatement(ReadLineToken);
        interpreter.RemoveStatement(ReadKeyToken);

        // sleep <milliseconds>: the script goes on once they have passed.
        interpreter.AddStatement(
            new StatementInformation("sleep", SearchMode.StartOfLine, SpaceAround.End),
            (args, context) =>
            {
                if (!int.TryParse(args.Trim(), out int milliseconds) || milliseconds < 0)
                {
                    context.Exit($"[sleep] Not a number of milliseconds: {args.Trim()}", true);
                    return;
                }

                // A background task (a line ending in `task`) has a thread of its own to wait on.
                if (!MainLoop.IsMainThread)
                {
                    Thread.Sleep(milliseconds);
                    return;
                }

                sleepUntil = Environment.TickCount64 + milliseconds;
            });

        // ui_wait [milliseconds]: the script goes on once the user does something in its
        // windows (YesNtWindowStatements.EventCount), or the time has passed.
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

                if (!MainLoop.IsMainThread)
                {
                    context.Exit("Only the script itself can wait for its windows, not a background task.", true);
                    return;
                }

                waitingForEvent = windowStatements.EventCount;
                eventWaitUntil = milliseconds > 0 ? Environment.TickCount64 + milliseconds : 0;
            });

        // %read_line is a line the user enters, %read_key the first character of one (a
        // terminal hands over whole lines).
        foreach (string token in new[] { ReadLineToken, ReadKeyToken })
        {
            interpreter.AddStatement(
                new StatementInformation(token, SearchMode.Contains, SpaceAround.None)
                { KeepStatementInArgs = true, Priority = Priority.Highest },
                SubstituteInput);
        }
    }

    // Puts the lines read for this line in place of its %read_line and %read_key, in order.
    // While too few have been read, asks for the next and has the line run again once it is
    // there: emptied, so no other statement acts on it, and a line back, which the interpreter
    // then steps forward to this line again.
    private void SubstituteInput(string args, IStatementContext context)
    {
        if (!MainLoop.IsMainThread)
        {
            context.Exit("Only the script itself can read input, not a background task.", true);
            return;
        }

        if (CountInputTokens(args) > readLines.Count)
        {
            if (ReadLine is null)
            {
                context.Exit("This script reads input, but runs without a terminal.", true);
                return;
            }

            pendingRead = ReadLine(TakePendingOutput(), readCancellation.Token);
            context.CurrentLine = string.Empty;
            context.LineNumber--;
            return;
        }

        string line = args;
        foreach (string read in readLines)
        {
            int lineAt = line.IndexOf(ReadLineToken, StringComparison.Ordinal);
            int keyAt = line.IndexOf(ReadKeyToken, StringComparison.Ordinal);
            line = keyAt >= 0 && (lineAt < 0 || keyAt < lineAt)
                ? StringExtensions.ReplaceFirstOccurrence(line, ReadKeyToken, StringExtensions.ToSafeString(read.Length > 0 ? read[..1] : " "))
                : StringExtensions.ReplaceFirstOccurrence(line, ReadLineToken, StringExtensions.ToSafeString(read));
        }

        readLines.Clear();
        context.CurrentLine = line.TrimEnd();
    }

    private static int CountInputTokens(string line)
    {
        int count = 0;
        foreach (string token in new[] { ReadLineToken, ReadKeyToken })
        {
            for (int at = line.IndexOf(token, StringComparison.Ordinal); at >= 0; at = line.IndexOf(token, at + token.Length, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    // Output written without a line end (a question) becomes the prompt the line is read
    // after, so it stands where the user types.
    private string TakePendingOutput()
    {
        string prompt = pendingOutput.ToString();
        _ = pendingOutput.Clear();
        return prompt;
    }

    private bool TryPrepare()
    {
        if (!File.Exists(scriptPath))
        {
            PrintLine($"Script not found: {scriptPath}");
            return false;
        }

        try
        {
            // The interpreter opens the script itself, so reading it is checked here first.
            scriptPath = UserFiles.Authorize(Caller, scriptPath, Access.Read);

            // Debug mode routes output through OnDebugOutput instead of Console,
            // so scripts started from the GUI terminal print there.
            interpreter.Prepare(scriptPath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PrintLine($"Could not load {scriptPath}: {ex.Message}");
            return false;
        }
    }

    private void WriteOutput(string text)
    {
        _ = pendingOutput.Append(text);

        string buffered = pendingOutput.ToString();
        int lineEnd = buffered.LastIndexOf('\n');

        if (lineEnd < 0)
        {
            return;
        }

        foreach (string line in buffered[..lineEnd].Split('\n'))
        {
            PrintLine(line.TrimEnd('\r'));
        }

        _ = pendingOutput.Clear().Append(buffered[(lineEnd + 1)..]);
    }

    private void FlushOutput()
    {
        if (pendingOutput.Length > 0)
        {
            PrintLine(pendingOutput.ToString());
            _ = pendingOutput.Clear();
        }
    }

    private void PrintLine(string line)
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
}
