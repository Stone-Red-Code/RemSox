using RemSox.Processing;
using RemSox.UI.CLI;

namespace RemSox.Processes;

internal class CliProcess() : Process("Cli")
{
    internal override void Run(string[] args)
    {
        Console.Clear();
        Console.WriteLine("Welcome RemSox!");
        Console.WriteLine("Type 'help' to see available commands.");

        while (!StopRequested)
        {
            Console.Write("> ");

            string? input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            bool handled = CommandManager.TryExecute(input, line => Console.WriteLine(line));

            if (!handled)
            {
                Console.WriteLine($"\"{input}\" is not a command");
            }
        }
    }
}
