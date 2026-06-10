using RemSox.Processing;
using RemSox.UI.CLI;

namespace RemSox.Processes;

internal class CliProcess() : Process("Cli")
{
    private string currentInput = string.Empty;

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

                default:
                    currentInput += key.KeyChar;
                    Console.Write(key.KeyChar);
                    break;
            }
        }
    }

    internal override void Stop()
    {
        Console.Clear();
    }

    private static void HandleCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return;

        bool handled = CommandManager.TryExecute(
            input,
            line => Console.WriteLine(line));

        if (!handled)
        {
            Console.WriteLine($"\"{input}\" is not a command");
        }
    }
}