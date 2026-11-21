namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// What every shell does with its keys: edit the line at the prompt and run it, answer a command's
/// question, cancel with Ctrl+C, and hand every key to a full-screen program while one runs. The
/// local console, Terminal windows and Telnet clients each feed it their keys and show its text on
/// their own <see cref="IShellTerminal"/>.
/// </summary>
/// <remarks>
/// Keys typed while a command runs are kept, and used once it asks a question, takes over the
/// screen or finishes, so typing ahead loses nothing. Everything here runs on the main loop.
/// </remarks>
public sealed class ShellFrontEnd : ICommandConsole, ITextScreen
{
    private const string Prompt = "> ";
    private const char ControlC = '\x03';

    // Typed-ahead keys beyond this are dropped, so a stuck command cannot fill the memory.
    private const int MaxTypeAhead = 256;

    private readonly IShellTerminal terminal;
    private readonly CommandRunner runner;
    private readonly LineEditor editor;
    private readonly LineEditor answerEditor;
    private readonly Queue<ConsoleKeyInfo> typeAhead = new();

    // A command's pending question (ReadLineAsync) and its prompt; the next line typed answers it.
    private TaskCompletionSource<string?>? pendingAnswer;
    private string answerPrompt = string.Empty;

    // Whether the pending answer is a password: typed but never shown, and not recalled from history.
    private bool answerHidden;

    // While a full-screen program (ITextScreen) runs, every key goes to it.
    private readonly KeyQueue screenKeys = new();
    private bool screenMode;

    // Set by Close: the shell's terminal belongs to something else now (the login prompt, after
    // logout), so the command that was running must not draw a prompt on it any more.
    private bool closed;

    public ShellFrontEnd(ShellSession session, IShellTerminal terminal)
    {
        this.terminal = terminal;
        Session = session;
        runner = new CommandRunner(session, this);
        editor = new LineEditor(session);
        answerEditor = new LineEditor(session);
        terminal.Resized += OnTerminalResized;
    }

    public ShellSession Session { get; }

    /// <summary>Raised on the main loop after each command, with its exit code (also after <c>exit</c>).</summary>
    public event Action<int>? CommandFinished;

    /// <summary>Shows the prompt; call once the shell's welcome text is written.</summary>
    public void Start()
    {
        ShowInput();
    }

    /// <summary>Runs <paramref name="line"/> as if it had been typed at the prompt.</summary>
    /// <returns>The command's exit code.</returns>
    public Task<int> RunAsync(string line)
    {
        terminal.ShowInput(Prompt + line);
        terminal.CommitInput();
        return ExecuteAsync(line);
    }

    /// <summary>Cancels the running command and wakes a full-screen program, as the shell closes.</summary>
    public void Close()
    {
        closed = true;
        terminal.Resized -= OnTerminalResized;
        typeAhead.Clear();
        runner.Cancel();
        screenKeys.Close();
    }

    /// <summary>Handles one key typed in this shell.</summary>
    public void HandleKey(ConsoleKeyInfo key)
    {
        if (screenMode)
        {
            // A full-screen program gets every key, Ctrl+C included.
            screenKeys.Enqueue(key);
            return;
        }

        if (key.KeyChar == ControlC || (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)))
        {
            // Cancels the running command, with any question it is asking, and what was typed ahead.
            typeAhead.Clear();
            runner.Cancel();
            return;
        }

        if (pendingAnswer is { } answer)
        {
            if (key.Key == ConsoleKey.Enter)
            {
                terminal.CommitInput();
                pendingAnswer = null;
                _ = answer.TrySetResult(answerEditor.Submit());
            }
            else if (answerHidden && key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
            {
                // A password is never filled in from the history.
            }
            else
            {
                Edit(answerEditor, key);
            }

            ShowInput();
            return;
        }

        if (runner.IsRunning)
        {
            if (typeAhead.Count < MaxTypeAhead)
            {
                typeAhead.Enqueue(key);
            }

            return;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            string line = editor.Submit();
            terminal.CommitInput();
            _ = ExecuteAsync(line);
            return;
        }

        Edit(editor, key);
        ShowInput();
    }

    private async Task<int> ExecuteAsync(string line)
    {
        Task<int> run = runner.RunAsync(line);
        ShowInput();

        int exitCode = await run;

        CommandFinished?.Invoke(exitCode);
        ShowInput();
        ReplayTypeAhead();
        return exitCode;
    }

    private static void Edit(LineEditor lineEditor, ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Backspace:
                lineEditor.Backspace();
                break;

            case ConsoleKey.UpArrow:
                lineEditor.HistoryPrevious();
                break;

            case ConsoleKey.DownArrow:
                lineEditor.HistoryNext();
                break;

            default:
                lineEditor.Insert(key.KeyChar);
                break;
        }
    }

    /// <summary>Shows the input line for the shell's state: a question, the prompt, or none while a command runs.</summary>
    private void ShowInput()
    {
        // No prompt in a shell that is closing after exit, or closed.
        if (screenMode || closed || Session.ExitRequested)
        {
            return;
        }

        if (pendingAnswer is not null)
        {
            terminal.ShowInput(answerPrompt + ShownAnswer);
        }
        else
        {
            terminal.ShowInput(runner.IsRunning ? null : Prompt + editor.Text);
        }
    }

    /// <summary>
    /// Hands the keys typed while a command ran to whatever reads keys now. Only the keys queued
    /// so far: one that starts another command queues the rest again.
    /// </summary>
    private void ReplayTypeAhead()
    {
        for (int count = typeAhead.Count; count > 0 && typeAhead.TryDequeue(out ConsoleKeyInfo key); count--)
        {
            HandleKey(key);
        }
    }

    // --- ICommandConsole ---

    public void WriteLine(string text)
    {
        terminal.WriteLine(text);
    }

    public void WriteError(string text)
    {
        terminal.WriteError(text);
    }

    public void Clear()
    {
        terminal.Clear();
    }

    public Task<string?> ReadLineAsync(string prompt, CancellationToken cancellation)
    {
        return ReadAnswerAsync(prompt, hidden: false, cancellation);
    }

    public Task<string?> ReadSecretAsync(string prompt, CancellationToken cancellation)
    {
        return ReadAnswerAsync(prompt, hidden: true, cancellation);
    }

    /// <summary>What of the answer being typed is shown: nothing of a password.</summary>
    private string ShownAnswer => answerHidden ? string.Empty : answerEditor.Text;

    private async Task<string?> ReadAnswerAsync(string prompt, bool hidden, CancellationToken cancellation)
    {
        TaskCompletionSource<string?> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        answerPrompt = prompt;
        answerHidden = hidden;
        answerEditor.Reset();
        pendingAnswer = answer;
        ShowInput();
        ReplayTypeAhead();

        using (cancellation.Register(() =>
        {
            if (answer.TrySetResult(null))
            {
                // Keep the question on screen, marked as cancelled, so what follows it makes sense.
                pendingAnswer = null;
                if (!screenMode)
                {
                    terminal.ShowInput(answerPrompt + ShownAnswer + "^C");
                    terminal.CommitInput();
                }

                ShowInput();
            }
        }))
        {
            return await answer.Task;
        }
    }

    public ITextScreen? Screen => this;

    // --- ITextScreen ---

    public int Width => terminal.Width;

    public int Height => terminal.Height;

    public event Action? Resized;

    /// <summary>Blanks the screen at its new size and lets the full-screen program draw it again.</summary>
    private void OnTerminalResized()
    {
        if (screenMode)
        {
            terminal.BeginScreen();
            Resized?.Invoke();
        }
    }

    public void Begin()
    {
        screenMode = true;
        terminal.BeginScreen();

        while (typeAhead.TryDequeue(out ConsoleKeyInfo key))
        {
            screenKeys.Enqueue(key);
        }
    }

    public void End()
    {
        screenMode = false;
        screenKeys.Close();
        terminal.EndScreen();
        ShowInput();
    }

    public void WriteAt(int column, int row, string text, bool inverse = false)
    {
        terminal.WriteAt(column, row, text, inverse);
    }

    public void SetCursor(int column, int row)
    {
        terminal.SetCursor(column, row);
    }

    public Task<ConsoleKeyInfo?> ReadKeyAsync(CancellationToken cancellation)
    {
        return screenKeys.ReadAsync(cancellation);
    }
}
