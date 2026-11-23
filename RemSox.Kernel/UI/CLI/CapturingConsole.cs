using System.Text;

namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// The console of a command whose output is collected instead of shown (<c>write -c</c>): its
/// lines are kept, while errors and questions still go to the shell's own console, so a
/// password prompt or a failure is seen rather than written into a file.
/// </summary>
/// <remarks>A full-screen program gets no screen here, so it says it cannot run.</remarks>
public sealed class CapturingConsole(ICommandConsole inner) : ICommandConsole
{
    private readonly StringBuilder output = new();

    /// <summary>Gets the output so far, each line ending with a newline.</summary>
    public string Text => output.ToString();

    public void WriteLine(string text)
    {
        _ = output.Append(text).Append(Environment.NewLine);
    }

    public void WriteError(string text)
    {
        inner.WriteError(text);
    }

    /// <summary>Does nothing: collected output has no screen to clear.</summary>
    public void Clear()
    {
    }

    public Task<string?> ReadLineAsync(string prompt, CancellationToken cancellation)
    {
        return inner.ReadLineAsync(prompt, cancellation);
    }

    public Task<string?> ReadSecretAsync(string prompt, CancellationToken cancellation)
    {
        return inner.ReadSecretAsync(prompt, cancellation);
    }
}
