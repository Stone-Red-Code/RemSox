using Cosmos.Kernel.System.Input;

using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;
using RemSox.Kernel.UI.GUI.UIEelements.Shapes;
using RemSox.Kernel.UI.GUI.Windows;

using System.Drawing;

namespace RemSox.Kernel.Processes;

/// <summary>
/// A shell in a desktop window. Each Terminal window is a shell of its own, with its own working
/// directory and history.
/// </summary>
public sealed class TerminalProcess() : Process("Terminal"), ICommandConsole
{
    private const string Prompt = "> ";
    private const int LineHeight = 30;
    private static readonly Color OutputColor = Color.LightGreen;
    private static readonly Color ErrorColor = Color.FromArgb(255, 110, 100);

    private static int terminalCount;

    private Window window = null!;
    private ShellSession session = null!;
    private LineEditor editor = null!;
    private LineEditor answerEditor = null!;
    private CommandRunner runner = null!;
    private readonly List<(string Text, Color Color)> outputLines = [];
    private readonly List<Text> textLines = [];
    private readonly Lock textLinesLock = new();
    private Size lastSize = new(-1, -1);
    private volatile bool displayDirty = true;

    // A command's pending question (ReadLineAsync) and its prompt; the next line typed answers it.
    private volatile TaskCompletionSource<string?>? pendingAnswer;
    private string answerPrompt = string.Empty;

    internal override void Start(string[] args)
    {
        session = new ShellSession("terminal" + Interlocked.Increment(ref terminalCount), canExit: true);
        editor = new LineEditor(session);
        answerEditor = new LineEditor(session);
        runner = new CommandRunner(session, this);

        window = CreateWindow("Terminal", new Size(400, 300));

        WriteLine("RemSox GUI Terminal v1.0");
        WriteLine("Type 'help' for commands.");

        window.Flush();
        window.OnKeyEvent += HandleKey;
    }

    internal override void Tick()
    {
        if (window.Size != lastSize && !window.IsResizing)
        {
            lastSize = window.Size;
            displayDirty = true;
        }

        // Redraw at most once per tick: a command printing many lines would otherwise redraw
        // (and send to remote viewers) the whole terminal once per line.
        if (displayDirty)
        {
            displayDirty = false;
            UpdateDisplay();
        }
    }

    internal override void Stop()
    {
        window.OnKeyEvent -= HandleKey;
        runner.Cancel();
    }

    private void HandleKey(KeyEvent keyEvent)
    {
        if (keyEvent.Key == Key.C && keyEvent.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            // Cancels the running command, and with it any question it is asking.
            runner.Cancel();
            return;
        }

        if (pendingAnswer is { } answer)
        {
            if (keyEvent.Key == Key.Enter)
            {
                string text = answerEditor.Submit();
                pendingAnswer = null;
                AddLine(answerPrompt + text, OutputColor);
                _ = answer.TrySetResult(text);
            }
            else
            {
                Edit(answerEditor, keyEvent);
            }

            displayDirty = true;
            return;
        }

        if (runner.IsRunning)
        {
            return;
        }

        if (keyEvent.Key == Key.Enter)
        {
            string line = editor.Submit();
            AddLine(Prompt + line, OutputColor);
            _ = RunAsync(line);
        }
        else
        {
            Edit(editor, keyEvent);
        }

        displayDirty = true;
    }

    private async Task RunAsync(string line)
    {
        _ = await runner.RunAsync(line);

        if (session.ExitRequested)
        {
            RequestStop();
        }

        displayDirty = true;
    }

    private static void Edit(LineEditor lineEditor, KeyEvent keyEvent)
    {
        switch (keyEvent.Key)
        {
            case Key.Backspace:
                lineEditor.Backspace();
                break;

            case Key.UpArrow:
                lineEditor.HistoryPrevious();
                break;

            case Key.DownArrow:
                lineEditor.HistoryNext();
                break;

            default:
                lineEditor.Insert(keyEvent.KeyChar);
                break;
        }
    }

    private void AddLine(string text, Color color)
    {
        lock (textLinesLock)
        {
            outputLines.Add((text, color));
            if (outputLines.Count > 1000)
            {
                outputLines.RemoveAt(0);
            }
        }

        displayDirty = true;
    }

    // --- ICommandConsole ---

    public void WriteLine(string text)
    {
        AddLine(text, OutputColor);
    }

    public void WriteError(string text)
    {
        AddLine(text, ErrorColor);
    }

    public void Clear()
    {
        lock (textLinesLock)
        {
            outputLines.Clear();
        }

        displayDirty = true;
    }

    public async Task<string?> ReadLineAsync(string prompt, CancellationToken cancellation)
    {
        TaskCompletionSource<string?> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        answerPrompt = prompt;
        answerEditor.Reset();
        pendingAnswer = answer;
        displayDirty = true;

        using (cancellation.Register(() =>
        {
            if (answer.TrySetResult(null))
            {
                pendingAnswer = null;
                displayDirty = true;
            }
        }))
        {
            return await answer.Task;
        }
    }

    // --- Display ---

    /// <summary>The bottom line: the prompt being answered, the command prompt, or nothing while a command runs.</summary>
    private string GetInputLine()
    {
        if (pendingAnswer is not null)
        {
            return answerPrompt + answerEditor.Text + "_";
        }

        return runner.IsRunning ? string.Empty : Prompt + editor.Text + "_";
    }

    private void UpdateDisplay()
    {
        if (window is null)
        {
            return;
        }

        int availableHeight = window.Size.Height - 24; // 18 for title + 6 margin
        int maxLines = Math.Max(1, (availableHeight / LineHeight) - 1); // -1 for the input line

        lock (textLinesLock)
        {
            // Enough Text elements for maxLines + 1 (the input line)
            while (textLines.Count <= maxLines)
            {
                Text textElement = window.CreateUIElement<Text>(t =>
                {
                    t.Color = OutputColor;
                    t.Content = "";
                    t.FontSize = LineHeight;
                });
                textLines.Add(textElement);
            }

            // Align everything flush to the bottom margin
            int startY = Math.Max(20, window.Size.Height - ((maxLines + 1) * LineHeight) - 5);

            for (int i = 0; i < maxLines; i++)
            {
                int lineIndex = outputLines.Count - maxLines + i;
                (string content, Color color) = lineIndex >= 0 && lineIndex < outputLines.Count
                    ? outputLines[lineIndex]
                    : (string.Empty, OutputColor);

                textLines[i].Content = content;
                textLines[i].Color = color;
                textLines[i].Position = new Point(5, startY + (i * LineHeight));
            }

            textLines[maxLines].Content = GetInputLine();
            textLines[maxLines].Color = OutputColor;
            textLines[maxLines].Position = new Point(5, startY + (maxLines * LineHeight));

            // Hide the Text elements a smaller window no longer needs
            for (int i = maxLines + 1; i < textLines.Count; i++)
            {
                textLines[i].Content = "";
            }
        }

        window.Flush();
    }
}
