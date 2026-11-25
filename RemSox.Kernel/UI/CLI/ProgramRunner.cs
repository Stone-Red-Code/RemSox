using RemSox.Kernel.Accounts;
using RemSox.Kernel.Processing;
using RemSox.Kernel.Programs;

namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// How a shell runs a program: started through <see cref="ProgramRegistry.Start"/> in the
/// shell's working directory, its outcome reported in the shell.
/// </summary>
/// <remarks>
/// A foreground program holds the shell until it ends: its output (<see cref="IConsoleOutput"/>)
/// goes to the shell, it reads lines typed there (<see cref="IConsoleInput"/>) and Ctrl+C stops it. Any other program runs beside the shell.
/// </remarks>
internal static class ProgramRunner
{
    /// <summary>Starts <paramref name="program"/> from a shell and, for a foreground program, waits for it.</summary>
    /// <returns>The exit code, as for a command.</returns>
    public static async Task<int> RunAsync(ProgramInfo program, IReadOnlyList<string> args, ShellSession session, Caller caller, ICommandConsole console, CancellationToken cancellation)
    {
        ProgramStart start = ProgramRegistry.Start(program, args, session.WorkingDirectory, caller);
        int reported = Report(start, console);
        if (start.Error is not null || !program.Foreground)
        {
            return reported;
        }

        if (ProcessManager.GetProcess(start.ProcessId) is IConsoleOutput output)
        {
            output.Output = console.WriteLine;
            output.Clear = console.Clear;
        }

        if (ProcessManager.GetProcess(start.ProcessId) is IConsoleInput input)
        {
            input.ReadLine = console.ReadLineAsync;
        }

        // The process ID is this run's alone, so Ctrl+C in one shell stops only its own program.
        using (cancellation.Register(() => ProcessManager.StopProcess(start.ProcessId)))
        {
            await ProcessManager.WaitForProcessExitAsync(start.ProcessId);
        }

        return cancellation.IsCancellationRequested ? CommandResult.Cancelled : CommandResult.Success;
    }

    /// <summary>Writes why something did not start, or what it says once started.</summary>
    /// <returns>The exit code: success, or failure when it did not start.</returns>
    public static int Report(ProgramStart start, ICommandConsole console)
    {
        if (start.Error is not null)
        {
            console.WriteError(start.Error);
            return CommandResult.Failure;
        }

        if (start.Message is not null)
        {
            console.WriteLine(start.Message);
        }

        return CommandResult.Success;
    }
}
