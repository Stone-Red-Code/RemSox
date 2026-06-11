using RemSox.Processing;
using RemSox.UI.CLI;

namespace RemSox.Processes;

internal class CliProcess() : Process("Cli")
{
    private string currentInput = string.Empty;

    private readonly List<string> commandHistory = [];
    private int historyIndex = -1;

    internal override void Start(string[] args)
    {
        Console.Clear();
        Console.WriteLine("Welcome RemSox!");
        Console.WriteLine("Type 'help' to see available commands.");
        Console.Write("> ");
    }

    internal override void Tick()
    {
        while (Console.KeyAvailable)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();

                    if (!string.IsNullOrWhiteSpace(currentInput) && (commandHistory.Count == 0 || commandHistory[^1] != currentInput))
                    {
                        commandHistory.Add(currentInput);
                    }

                    historyIndex = commandHistory.Count;

                    HandleCommand(currentInput);

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
                    if (commandHistory.Count > 0)
                    {
                        historyIndex = Math.Max(historyIndex - 1, 0);
                        currentInput = commandHistory[historyIndex];
                        Console.Write("\r> " + currentInput + new string('#', Console.WindowWidth - currentInput.Length - 2));
                        Console.CursorLeft = 0;
                        Console.CursorTop--;
                        Console.Write("> " + currentInput);
                    }
                    break;
                case ConsoleKey.DownArrow:
                    if (commandHistory.Count > 0)
                    {
                        historyIndex = Math.Min(historyIndex + 1, commandHistory.Count - 1);
                        currentInput = commandHistory[historyIndex];
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

    private static void HandleCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        bool handled = CommandManager.TryExecute(
            input,
            line => Console.WriteLine(line));

        if (!handled)
        {
            Console.WriteLine($"\"{input}\" is not a command");
        }
    }
}