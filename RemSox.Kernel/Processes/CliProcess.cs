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
internal sealed class CliProcess() : Process("Cli"), ICommandConsole, ITextScreen
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

    // While a full-screen program (ITextScreen) runs, every key goes to it.
    private readonly KeyQueue fullScreenKeys = new();
    private volatile bool fullScreen;

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

            if (fullScreen)
            {
                fullScreenKeys.Enqueue(key);
                continue;
            }

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

    public ITextScreen? Screen => this;

    // --- ITextScreen ---

    public int Width => Console.WindowWidth;

    public int Height => Console.WindowHeight;

    public void Begin()
    {
        lock (consoleLock)
        {
            fullScreen = true;
            Console.Clear();
        }
    }

    public void End()
    {
        lock (consoleLock)
        {
            fullScreen = false;
            fullScreenKeys.Close();
            Console.ResetColor();
            Console.Clear();
        }
    }

    public void WriteAt(int column, int row, string text, bool inverse = false)
    {
        lock (consoleLock)
        {
            // Never the last column: writing it moves the cursor on, and on the last row scrolls.
            int room = Console.WindowWidth - 1 - column;
            if (room <= 0 || row < 0 || row >= Console.WindowHeight)
            {
                return;
            }

            Console.SetCursorPosition(column, row);

            if (inverse)
            {
                Console.ForegroundColor = ConsoleColor.Black;
                Console.BackgroundColor = ConsoleColor.Gray;
            }

            Console.Write(text.Length > room ? text[..room] : text);

            if (inverse)
            {
                Console.ResetColor();
            }
        }
    }

    public void SetCursor(int column, int row)
    {
        lock (consoleLock)
        {
            Console.SetCursorPosition(Math.Clamp(column, 0, Console.WindowWidth - 1), Math.Clamp(row, 0, Console.WindowHeight - 1));
        }
    }

    public Task<ConsoleKeyInfo?> ReadKeyAsync(CancellationToken cancellation)
    {
        return fullScreenKeys.ReadAsync(cancellation);
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
