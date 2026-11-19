using RemSox.Kernel.Processing;
using RemSox.Kernel.Programs;

namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// A program seen by the shell as a command, so argument checks, <c>help</c> and usage work for
/// it as for any command. Typing its name starts it through <see cref="ProgramRegistry.Start"/>
/// in the shell's working directory.
/// </summary>
/// <remarks>
/// A foreground program holds the shell until it ends: its output (<see cref="IConsoleOutput"/>)
/// goes to the shell and Ctrl+C stops it. Any other program runs beside the shell.
/// </remarks>
internal sealed class ProgramCommand(ProgramInfo program) : ICommand
{
    public string Name => program.Name;

    public string Description => program.Description;

    public string Usage => program.Usage;

    public int MinArguments => program.MinArguments;

    public int MaxArguments => program.MaxArguments;

    public async Task<int> ExecuteAsync(CommandContext context)
    {
        ProgramStart start = ProgramRegistry.Start(program, context.Args, context.Session.WorkingDirectory);
        if (start.Error is not null)
        {
            return context.Fail(start.Error);
        }

        if (start.Message is not null)
        {
            context.Console.WriteLine(start.Message);
        }

        if (!program.Foreground)
        {
            return CommandResult.Success;
        }

        if (ProcessManager.GetProcess(start.ProcessId) is IConsoleOutput output)
        {
            output.Output = context.Console.WriteLine;
        }

        // The process ID is this run's alone, so Ctrl+C in one shell stops only its own program.
        using (context.Cancellation.Register(() => ProcessManager.StopProcess(start.ProcessId)))
        {
            await ProcessManager.WaitForProcessExitAsync(start.ProcessId);
        }

        return context.Cancellation.IsCancellationRequested ? CommandResult.Cancelled : CommandResult.Success;
    }
}
