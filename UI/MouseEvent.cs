namespace RemSox.UI;

public record MouseEvent(MouseEventType Type, int X = 0, int Y = 0, MouseButton Button = MouseButton.None, int Delta = 0)
{
    public static MouseEvent Move(int x, int y)
    {
        return new(MouseEventType.Move, x, y);
    }

    public static MouseEvent ButtonDown(int x, int y, MouseButton button)
    {
        return new(MouseEventType.ButtonDown, x, y, button);
    }

    public static MouseEvent ButtonUp(int x, int y, MouseButton button)
    {
        return new(MouseEventType.ButtonUp, x, y, button);
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