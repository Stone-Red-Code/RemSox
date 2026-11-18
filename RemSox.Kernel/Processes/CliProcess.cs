using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Processes;

/// <summary>
/// The text-mode shell on the local console.
/// </summary>
/// <remarks>
/// Keys are read without blocking from <see cref="Tick"/>, on the kernel's main loop: a blocking
/// Console.ReadLine would keep reading the keyboard after the desktop starts and steal its keys.
/// </remarks>
internal class CliProcess() : Process("Cli")
{
    private const string Prompt = "> ";

    private readonly LineEditor editor = new();
    private readonly CommandRunner runner = new();

    internal override void Start(string[] args)
    {
        Console.Clear();
        Console.WriteLine("Welcome RemSox!");
        Console.WriteLine("Type 'help' to see available commands.");
        Console.WriteLine(Environment.ProcessorCount + " CPU cores detected");
        Console.Write(Prompt);
    }

    internal override async void Tick()
    {
        while (Console.KeyAvailable)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (runner.IsRunning)
            {
                if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                {
                    await runner.CancelAsync();
                }

                continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    await runner.RunAsync(editor.Submit(), Console.WriteLine);
                    Console.Write(Prompt);
                    break;

                case ConsoleKey.Backspace:
                    if (editor.Text.Length > 0)
                    {
                        editor.Backspace();
                        Console.Write("\b \b");
                    }
                    break;

                case ConsoleKey.UpArrow:
                    ReplaceLine(editor.HistoryPrevious);
                    break;

                case ConsoleKey.DownArrow:
                    ReplaceLine(editor.HistoryNext);
                    break;

                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        editor.Insert(key.KeyChar);
                        Console.Write(key.KeyChar);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Applies a change that replaces the whole input line, then redraws the line in place.
    /// </summary>
    private void ReplaceLine(Action change)
    {
        int oldLength = editor.Text.Length;
        change();

        string blank = new(' ', Math.Max(0, oldLength - editor.Text.Length));
        Console.Write("\r" + Prompt + editor.Text + blank + new string('\b', blank.Length));
    }
}
