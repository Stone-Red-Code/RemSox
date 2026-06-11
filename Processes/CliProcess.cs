using RemSox.Processing;
using RemSox.UI.CLI;

namespace RemSox.Processes;

internal class CliProcess() : Process("Cli")
{
    private string currentInput = string.Empty;

    private int historyIndex = -1;

    private bool commandRunning = false;

    internal override void Start(string[] args)
    {
        Console.Clear();
        Console.WriteLine("Welcome RemSox!");
        Console.WriteLine("Type 'help' to see available commands.");
        Console.Write("> ");

        historyIndex = CommandManager.GetCommandHistory().Count;
    }

    internal override async void Tick()
    {
        while (Console.KeyAvailable && !commandRunning)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();

                    await HandleCommand(currentInput);

                    historyIndex = CommandManager.GetCommandHistory().Count;

                    currentInput = string.Empty;
                    Console.Write("> ");
                    break;

                case ConsoleKey.Backspace:
                    if (currentInput.Length > 0)
                    {
                        currentInput = currentInput[..^1];
                        Console.Write("\b \b");
                    }
                    break;
                case ConsoleKey.UpArrow:
                    if (CommandManager.GetCommandHistory().Count > 0)
                    {
                        historyIndex = Math.Max(historyIndex - 1, 0);
                        currentInput = CommandManager.GetCommandHistory()[historyIndex];
                        Console.Write("\r> " + currentInput + new string('#', Console.WindowWidth - currentInput.Length - 2));
                        Console.CursorLeft = 0;
                        Console.CursorTop--;
                        Console.Write("> " + currentInput);
                    }
                    break;
                case ConsoleKey.DownArrow:
                    if (CommandManager.GetCommandHistory().Count > 0)
                    {
                        historyIndex = Math.Min(historyIndex + 1, CommandManager.GetCommandHistory().Count - 1);
                        currentInput = CommandManager.GetCommandHistory()[historyIndex];
                        Console.Write("\r> " + currentInput + new string('#', Console.WindowWidth - currentInput.Length - 2));
                        Console.CursorLeft = 0;
                        Console.CursorTop--;
                        Console.Write("> " + currentInput);
                    }
                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        currentInput += key.KeyChar;
                        Console.Write(key.KeyChar);
                    }
                    break;
            }
        }
    }

    private async Task HandleCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        commandRunning = true;

        bool handled = await CommandManager.TryExecute(input, Console.WriteLine);

        if (!handled)
        {
            Console.WriteLine($"\"{input}\" is not a command");
        }

        commandRunning = false;
    }
}