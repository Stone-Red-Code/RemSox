using System.Drawing;

using Cosmos.Kernel.System.Input;

using RemSox.Kernel.UI.GUI.Rendering;
using RemSox.Kernel.UI.GUI.Windows;
using RemSox.Shared.UI;

namespace RemSox.Kernel.UI.GUI;

/// <summary>
/// The machine's own display, mouse and keyboard in graphics mode. It shows one
/// <see cref="Screen"/> at a time, the one the kernel gives it (<see cref="Show"/>): the desktop
/// of whoever is logged in at the machine, or the login window's. Another session's windows,
/// such as a remote desktop user's, are never drawn here and get none of its input.
/// </summary>
public static class LocalDisplay
{
    private static CanvasRenderSource? canvas;

    private static Point lastMousePosition = Point.Empty;
    private static bool lastLeft, lastRight, lastMiddle;

    /// <summary>Gets the screen the display shows, or null while the machine is in text mode.</summary>
    public static Screen? Shown { get; private set; }

    /// <summary>
    /// Shows <paramref name="screen"/>, or nothing (text mode) for null. The previous screen's
    /// windows leave the display and the new screen's draw themselves on it.
    /// </summary>
    public static void Show(Screen? screen)
    {
        if (Shown == screen)
        {
            return;
        }

        if (Shown is not null && canvas is not null)
        {
            Shown.RemoveRenderSource(canvas);
            canvas.Clear();
        }

        Shown = screen;

        if (screen is not null)
        {
            canvas ??= new CanvasRenderSource();
            screen.AddRenderSource(canvas);
            screen.InvalidateAll();
        }
    }

    /// <summary>
    /// Hands the local mouse and keyboard to the shown screen. Called every pass of the main loop
    /// while a screen is shown; in text mode the console reads the keyboard instead.
    /// </summary>
    public static void PollInput()
    {
        if (Shown is not { } screen)
        {
            return;
        }

        Point position = new(MouseManager.X, MouseManager.Y);

        if (position != lastMousePosition)
        {
            screen.EnqueueMouseEvent(MouseEvent.Move(position.X, position.Y));
            lastMousePosition = position;
        }

        PollButton(MouseManager.LeftButton, ref lastLeft, MouseButton.Left);
        PollButton(MouseManager.RightButton, ref lastRight, MouseButton.Right);
        PollButton(MouseManager.MiddleButton, ref lastMiddle, MouseButton.Middle);

        int scroll = MouseManager.ScrollDelta;
        if (scroll != 0)
        {
            MouseManager.ResetScrollDelta();
            screen.EnqueueMouseEvent(MouseEvent.Wheel(position.X, position.Y, scroll));
        }

        while (KeyboardManager.TryReadKey(out KeyEvent? keyEvent) && keyEvent is not null)
        {
            screen.EnqueueKeyEvent(keyEvent);
        }

        void PollButton(bool pressed, ref bool last, MouseButton button)
        {
            if (pressed != last)
            {
                ConsoleModifiers modifiers = KeyboardManager.IsEnabled
                    ? (KeyboardManager.ShiftPressed ? ConsoleModifiers.Shift : 0)
                        | (KeyboardManager.AltPressed ? ConsoleModifiers.Alt : 0)
                        | (KeyboardManager.ControlPressed ? ConsoleModifiers.Control : 0)
                    : 0;
                screen.EnqueueMouseEvent(pressed ? MouseEvent.ButtonDown(position.X, position.Y, button, modifiers) : MouseEvent.ButtonUp(position.X, position.Y, button, modifiers));
                last = pressed;
            }
        }
    }
}
