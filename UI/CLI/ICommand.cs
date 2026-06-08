namespace RemSox.UI.GUI.CLI;

public interface ICommand
{
    string Name { get; }

    string Description { get; }

    void Execute(string? arguments);
}
