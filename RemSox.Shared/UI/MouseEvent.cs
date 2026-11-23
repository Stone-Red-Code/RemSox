namespace RemSox.Shared.UI;

/// <param name="Modifiers">
/// The keys held down with a button press or release (Shift, Ctrl, Alt), for a click that
/// means something else with one (Ctrl+click adds to a selection).
/// </param>
public record MouseEvent(MouseEventType Type, int X = 0, int Y = 0, MouseButton Button = MouseButton.None, int Delta = 0, ConsoleModifiers Modifiers = 0)
{
    public static MouseEvent Move(int x, int y)
    {
        return new(MouseEventType.Move, x, y);
    }

    public static MouseEvent ButtonDown(int x, int y, MouseButton button, ConsoleModifiers modifiers = 0)
    {
        return new(MouseEventType.ButtonDown, x, y, button, Modifiers: modifiers);
    }

    public static MouseEvent ButtonUp(int x, int y, MouseButton button, ConsoleModifiers modifiers = 0)
    {
        return new(MouseEventType.ButtonUp, x, y, button, Modifiers: modifiers);
    }

    public static MouseEvent Wheel(int x, int y, int delta)
    {
        return new(MouseEventType.Wheel, x, y, Delta: delta);
    }
}

public enum MouseEventType
{
    Move,
    ButtonDown,
    ButtonUp,
    Wheel
}
public enum MouseButton
{
    Left,
    Right,
    Middle,
    None
}
