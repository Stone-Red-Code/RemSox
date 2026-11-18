using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Processes;

/// <summary>
/// The text-mode shell on the local console.
/// </summary>
/// <remarks>
/// Keys are read without blocking from <see cref="Tick"/>, on the kernel's main loop: a blocking
/// Console.ReadLine would keep reading the keyboard after the desktop starts and steal its keys.
/// A command runs without holding up the loop, so later ticks can deliver Ctrl+C and the answers
/// to its questions.
/// </remarks>
internal sealed class CliProcess() : Process("Cli"), ICommandConsole
{
    private const string Prompt = "> ";

    private readonly ShellSession session = new("tty1", canExit: false);
    private readonly Lock consoleLock = new();
    private LineEditor editor = null!;
    private LineEditor answerEditor = null!;
    private CommandRunner runner = null!;

    // A command's pending question (ReadLineAsync) and its prompt; the next line typed answers it.
    private volatile TaskCompletionSource<string?>? pendingAnswer;
    private string answerPrompt = string.Empty;

    internal override void Start(string[] args)
    {
        editor = new LineEditor(session);
        answerEditor = new LineEditor(session);
        runner = new CommandRunner(session, this);

        Console.Clear();
        Console.WriteLine("Welcome RemSox!");
        Console.WriteLine("Type 'help' to see available commands.");
        Console.WriteLine(Environment.ProcessorCount + " CPU cores detected");
        Console.Write(Prompt);
    }

    internal override void Tick()
    {
        while (Console.KeyAvailable)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            bool controlC = key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control);

            if (controlC)
            {
                // Cancels the running command, and with it any question it is asking.
                runner.Cancel();
                continue;
            }

            if (pendingAnswer is { } answer)
            {
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    pendingAnswer = null;
                    _ = answer.TrySetResult(answerEditor.Submit());
                }
                else
                {
                    Edit(answerEditor, answerPrompt, key);
                }

                continue;
            }

            if (runner.IsRunning)
            {
                continue;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                _ = RunAsync(editor.Submit());
            }
            else
            {
                Edit(editor, Prompt, key);
            }
        }
    }

    private async Task RunAsync(string line)
    {
        _ = await runner.RunAsync(line);
        Console.Write(Prompt);
    }

    private static void Edit(LineEditor lineEditor, string prompt, ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Backspace:
                if (lineEditor.Text.Length > 0)
                {
                    lineEditor.Backspace();
                    Console.Write("\b \b");
                }
                break;

            case ConsoleKey.UpArrow:
                ReplaceLine(lineEditor, prompt, lineEditor.HistoryPrevious);
                break;

            case ConsoleKey.DownArrow:
                ReplaceLine(lineEditor, prompt, lineEditor.HistoryNext);
                break;

            default:
                if (!char.IsControl(key.KeyChar))
                {
                    lineEditor.Insert(key.KeyChar);
                    Console.Write(key.KeyChar);
                }
                break;
        }
    }

    /// <summary>
    /// Applies a change that replaces the whole input line, then redraws the line in place.
    /// </summary>
    private static void ReplaceLine(LineEditor lineEditor, string prompt, Action change)
    {
        int oldLength = lineEditor.Text.Length;
        change();

        string blank = new(' ', Math.Max(0, oldLength - lineEditor.Text.Length));
        Console.Write("\r" + prompt + lineEditor.Text + blank + new string('\b', blank.Length));
    }

    // --- ICommandConsole ---

    public void WriteLine(string text)
    {
        lock (consoleLock)
        {
            Console.WriteLine(text);
        }
    }

    public void WriteError(string text)
    {
        lock (consoleLock)
        {
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(text);
            Console.ForegroundColor = previous;
        }
    }

    public void Clear()
    {
        lock (consoleLock)
        {
            Console.Clear();
        }
    }

    public async Task<string?> ReadLineAsync(string prompt, CancellationToken cancellation)
    {
        TaskCompletionSource<string?> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (consoleLock)
        {
            Console.Write(prompt);
        }

        answerPrompt = prompt;
        answerEditor.Reset();
        pendingAnswer = answer;

        using (cancellation.Register(() =>
        {
            if (answer.TrySetResult(null))
            {
                pendingAnswer = null;
                WriteLine(string.Empty);
            }
        }))
        {
            return await answer.Task;
        }
    }
}
