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
    private const int MaxLines = 15;
    private const int LineHeight = 16;

    public TerminalProcess() : base("Terminal")
    {
    }

    internal override void Run()
    {
        window = WindowManager.CreateWindow(this, "Terminal", new Point(50, 50), new Size(400, 300));

        for (int i = 0; i < MaxLines + 1; i++) // +1 for the input line
        {
            var textElement = window.CreateUIElement<Text>(t =>
            {
                t.Position = new Point(5, 20 + (i * LineHeight));
                t.Color = Color.LightGreen;
                t.Content = "";
            });
            textLines.Add(textElement);
        }

        window.AutoFlush = true;

        PrintLine("RemSox GUI Terminal v1.0");
        PrintLine("Type 'help' for commands.");

        window.OnKeyEvent += HandleKey;

        while (!StopRequested)
        {
            System.Threading.Thread.Sleep(50);
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
                // Temporarily capture console output
                // Wait, CommandManager uses Console.WriteLine.
                // Redirecting Console output in Cosmos might be tricky.
                // For now we will just execute it. Note: CommandManager commands write to standard Console, 
                // which might write over the VGA buffer or just be invisible.
                // To make a proper terminal, commands should return strings or we need a custom ICommand context.

                if (cmd == "exit")
                {
                    RequestStop();
                }
                else
                {
                    bool found = CommandManager.TryExecute(cmd);
                    if (!found)
                    {
                        PrintLine($"\"{cmd}\" is not a command");
                    }
                    else
                    {
                        PrintLine("Command executed. (Output sent to background console)");
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
        if (history.Count > MaxLines)
        {
            history.RemoveAt(0);
        }
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        for (int i = 0; i < MaxLines; i++)
        {
            if (i < history.Count)
            {
                textLines[i].Content = history[i];
            }
            else
            {
                textLines[i].Content = "";
            }
        }

        // The last line is the input line
        textLines[MaxLines].Content = "> " + currentInput + "_";
    }
}
