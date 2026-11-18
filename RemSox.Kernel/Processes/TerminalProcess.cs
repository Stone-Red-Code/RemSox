using Cosmos.Kernel.System.Input;

using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;
using RemSox.Kernel.UI.GUI.UIEelements.Shapes;
using RemSox.Kernel.UI.GUI.Windows;

using System.Drawing;

namespace RemSox.Kernel.Processes;

public class TerminalProcess() : Process("Terminal")
{
    private Window window = null!;
    private readonly List<string> outputLines = [];
    private readonly LineEditor editor = new();
    private readonly CommandRunner runner = new();
    private readonly List<Text> textLines = [];
    private readonly Lock textLinesLock = new();
    private const int LineHeight = 30;
    private Size lastSize = new(-1, -1);
    private volatile bool displayDirty = true;

    internal override void Start(string[] args)
    {
        window = CreateWindow("Terminal", new Size(400, 300));

        PrintLine("RemSox GUI Terminal v1.0");
        PrintLine("Type 'help' for commands.");

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
    }

    private async void HandleKey(KeyEvent keyEvent)
    {
        if (runner.IsRunning)
        {
            if (keyEvent.Key == Key.C && keyEvent.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                await runner.CancelAsync();
            }

            return;
        }

        switch (keyEvent.Key)
        {
            case Key.Enter:
                string line = editor.Submit();
                PrintLine("> " + line);

                switch (line.Trim())
                {
                    case "exit":
                        RequestStop();
                        break;

                    case "clear":
                        lock (textLinesLock)
                        {
                            outputLines.Clear();
                        }
                        break;

                    default:
                        await runner.RunAsync(line, PrintLine);
                        break;
                }
                break;

            case Key.Backspace:
                editor.Backspace();
                break;

            case Key.UpArrow:
                editor.HistoryPrevious();
                break;

            case Key.DownArrow:
                editor.HistoryNext();
                break;

            default:
                editor.Insert(keyEvent.KeyChar);
                break;
        }

        displayDirty = true;
    }

    private void PrintLine(string text)
    {
        lock (textLinesLock)
        {
            outputLines.Add(text);
            if (outputLines.Count > 1000)
            {
                outputLines.RemoveAt(0);
            }
        }

        displayDirty = true;
    }

    private void UpdateDisplay()
    {
        if (window is null)
        {
            return;
        }

        int availableHeight = window.Size.Height - 24; // 18 for title + 6 margin
        int maxLines = (availableHeight / LineHeight) - 1; // -1 for input line

        if (maxLines < 1)
        {
            maxLines = 1;
        }

        lock (textLinesLock)
        {
            // Ensure we have enough Text elements for maxLines + 1 (input line)
            while (textLines.Count <= maxLines)
            {
                Text textElement = window.CreateUIElement<Text>(t =>
                {
                    t.Color = Color.LightGreen;
                    t.Content = "";
                    t.FontSize = LineHeight;
                });
                textLines.Add(textElement);
            }

            // Calculate starting Y to align everything flush to the bottom margin
            int startY = window.Size.Height - ((maxLines + 1) * LineHeight) - 5;
            if (startY < 20)
            {
                startY = 20;
            }

            for (int i = 0; i < maxLines; i++)
            {
                int lineIndex = outputLines.Count - maxLines + i;
                string content = "";
                if (lineIndex >= 0 && lineIndex < outputLines.Count)
                {
                    content = outputLines[lineIndex];
                }

                textLines[i].Content = content;
                textLines[i].Position = new Point(5, startY + (i * LineHeight));
            }

            // The last line is the input line
            textLines[maxLines].Content = "> " + editor.Text + "_";
            textLines[maxLines].Position = new Point(5, startY + (maxLines * LineHeight));
            // Hide any extra text lines we don't need
            for (int i = maxLines + 1; i < textLines.Count; i++)
            {
                textLines[i].Content = "";
            }
        }

        window.Flush();
    }
}
