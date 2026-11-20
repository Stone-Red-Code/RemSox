using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Processes;

/// <summary>
/// The text-mode shell on the local console.
/// </summary>
/// <remarks>
/// Keys are read without blocking from <see cref="Tick"/>, on the kernel's main loop: a blocking
/// Console.ReadLine would keep reading the keyboard after the desktop starts and steal its keys.
/// A command runs without holding up the loop, so later ticks can deliver Ctrl+C and the answers
/// to its questions.
/// </remarks>
internal sealed class CliProcess() : Process("Cli")
{
    private ShellFrontEnd frontEnd = null!;

    internal override void Start(string[] args)
    {
        frontEnd = new ShellFrontEnd(new ShellSession("tty1", canExit: false, WorkingDirectory), new ConsoleTerminal());

        Console.Clear();
        frontEnd.WriteLine("Welcome RemSox!");
        frontEnd.WriteLine("Type 'help' to see available commands.");
        frontEnd.WriteLine(Environment.ProcessorCount + " CPU cores detected");
        frontEnd.Start();
    }

    internal override void Tick()
    {
        while (Console.KeyAvailable)
        {
            frontEnd.HandleKey(Console.ReadKey(intercept: true));
        }
    }

    internal override void Stop()
    {
        frontEnd.Close();
    }
}
