using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System;
using Cosmos.Kernel.System.Keyboard;
using RemSox.Processing;
using RemSox.UI.GUI.UIEelements;
using RemSox.UI.GUI.Windows;
using RemSox.UI.GUI.CLI;

namespace RemSox.Processes;

public class TerminalProcess : Process
{
    private Window? window;
    private readonly List<string> history = new();
    private string currentInput = "";
    private readonly List<Text> textLines = new();
    private readonly object textLinesLock = new object();
    private const int LineHeight = 30;
    private Size lastSize = new Size(-1, -1);

    public TerminalProcess() : base("Terminal")
    {
    }

    internal override void Run()
    {
        window = WindowManager.CreateWindow(this, "Terminal", new Point(50, 50), new Size(400, 300));

        PrintLine("RemSox GUI Terminal v1.0");
        PrintLine("Type 'help' for commands.");

        window.Flush();
        window.OnKeyEvent += HandleKey;

        while (!StopRequested)
        {
            if (window.Size != lastSize)
            {
                lastSize = window.Size;
                UpdateDisplay();
            }
            Thread.Sleep(50);
        }

        window.OnKeyEvent -= HandleKey;
        WindowManager.CloseWindow(window);
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
                if (cmd == "exit")
                {
                    RequestStop();
                }
                else
                {
                    bool found = CommandManager.TryExecute(cmd, line => PrintLine(line));
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
                currentInput = currentInput.Substring(0, currentInput.Length - 1);
                UpdateDisplay();
            }
        }
        else if (keyEvent.KeyChar >= 32 && keyEvent.KeyChar <= 126) // Printable chars
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
        if (window == null) return;

        int availableHeight = window.Size.Height - 24; // 18 for title + 6 margin
        int maxLines = availableHeight / LineHeight - 1; // -1 for input line

        if (maxLines < 1) maxLines = 1;

        lock (textLinesLock)
        {
            // Ensure we have enough Text elements for maxLines + 1 (input line)
            while (textLines.Count <= maxLines)
            {
                var textElement = window.CreateUIElement<Text>(t =>
                {
                    t.Color = Color.LightGreen;
                    t.Content = "";
                });
                textLines.Add(textElement);
            }

            // Calculate starting Y to align everything flush to the bottom margin
            int startY = window.Size.Height - ((maxLines + 1) * LineHeight) - 5;
            if (startY < 20) startY = 20;

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

            window.Flush();
        }
    }
}