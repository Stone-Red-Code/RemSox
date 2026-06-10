using Cosmos.Kernel.System.Keyboard;

using RemSox.Processing;
using RemSox.UI.CLI;
using RemSox.UI.GUI.UIEelements;
using RemSox.UI.GUI.Windows;

using System.Drawing;

namespace RemSox.Processes;

public class TerminalProcess() : Process("Terminal")
{
    private Window window = null!;
    private readonly List<string> history = [];
    private string currentInput = "";
    private readonly List<Text> textLines = [];
    private readonly Lock textLinesLock = new();
    private const int LineHeight = 30;
    private Size lastSize = new(-1, -1);

    internal override void Start(string[] args)
    {
        window = WindowManager.CreateWindow(this, "Terminal", new Size(400, 300));

        PrintLine("RemSox GUI Terminal v1.0");
        PrintLine("Type 'help' for commands.");

        window.Flush();
        window.OnKeyEvent += HandleKey;
    }

    internal override void Tick()
    {
        if (window.Size != lastSize)
        {
            lastSize = window.Size;
            UpdateDisplay();
        }
    }

    internal override void Stop()
    {
        window.OnKeyEvent -= HandleKey;
    }

    private void HandleKey(KeyEvent keyEvent)
    {
        if (keyEvent.Key == ConsoleKeyEx.Enter)
        {
            string cmd = currentInput;
            PrintLine("> " + cmd);
            currentInput = "";

            if (!string.IsNullOrWhiteSpace(cmd))
            {
                if (cmd.Trim() == "exit")
                {
                    RequestStop();
                }
                else if (cmd.Trim() == "clear")
                {
                    lock (textLinesLock)
                    {
                        history.Clear();
                        UpdateDisplay();
                    }
                }
                else
                {
                    bool found = CommandManager.TryExecute(cmd, PrintLine);
                    if (!found)
                    {
                        PrintLine($"\"{cmd}\" is not a command");
                    }
                }
            }
            UpdateDisplay();
        }
        else if (keyEvent.Key == ConsoleKeyEx.Backspace)
        {
            if (currentInput.Length > 0)
            {
                currentInput = currentInput[..^1];
                UpdateDisplay();
            }
        }
        else if (!char.IsControl(keyEvent.KeyChar))
        {
            currentInput += keyEvent.KeyChar;
            UpdateDisplay();
        }
    }

    private void PrintLine(string text)
    {
        history.Add(text);
        if (history.Count > 1000)
        {
            history.RemoveAt(0);
        }
        UpdateDisplay();
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
                int historyIndex = history.Count - maxLines + i;
                string content = "";
                if (historyIndex >= 0 && historyIndex < history.Count)
                {
                    content = history[historyIndex];
                }

                textLines[i].Content = content;
                textLines[i].Position = new Point(5, startY + (i * LineHeight));
            }

            // The last line is the input line
            textLines[maxLines].Content = "> " + currentInput + "_";
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