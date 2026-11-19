using System.Text;

using RemSox.Kernel.Logging;
using RemSox.Kernel.Processing;
using RemSox.Kernel.Utils;

using YesNt.Interpreter.Runtime;

namespace RemSox.Kernel.Processes;

/// <summary>
/// Runs a YesNt script (.ynt) file. The script path is the first start argument.
/// </summary>
public class YesNtInterpreterProcess() : Process("YesNtInterpreter")
{
    public const string FileExtension = ".ynt";

    private const int LinesPerTick = 50;

    private readonly StringBuilder pendingOutput = new();

    private YesNtInterpreter interpreter = null!;

    private string? scriptPath;

    private bool prepared;

    /// <summary>
    /// Receives the script's output line by line. When unset, output goes to the process log.
    /// Must be set before the first tick to capture all output.
    /// </summary>
    public Action<string>? Output { get; set; }

    internal override void Start(string[] args)
    {
        interpreter = new();
        new YesNtWindowStatements(this).Register(interpreter);
        interpreter.OnDebugOutput += WriteOutput;

        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            Logger.Log("No script file given.", LogSeverity.Error);
            RequestStop();
            return;
        }

        scriptPath = Path.GetFullPath(args[0]);
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

        if (interpreter.IsRunning)
        {
            _ = interpreter.Step(LinesPerTick);
        }
        else
        {
            RequestStop();
        }
    }

    internal override void Stop()
    {
        interpreter.Stop();
        FlushOutput();
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
