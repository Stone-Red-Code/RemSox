namespace RemSox.UI.GUI.CLI;

using System;

/// <summary>
/// Defines a command executable via the CLI or GUI terminal.
/// </summary>
public interface ICommand
{
    /// <summary>
    /// Gets the name of the command used to invoke it.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a brief description of the command's functionality.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Executes the command.
    /// </summary>
    /// <param name="arguments">The arguments provided to the command.</param>
    /// <param name="printLine">A delegate to stream output lines to the current console or terminal.</param>
    void Execute(string? arguments, Action<string> printLine);
}
