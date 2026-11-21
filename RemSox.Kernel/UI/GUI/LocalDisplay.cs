using System.Drawing;

using Cosmos.Kernel.System.Input;

using RemSox.Kernel.UI.GUI.Rendering;
using RemSox.Kernel.UI.GUI.Windows;
using RemSox.Shared.UI;

namespace RemSox.Kernel.UI.GUI;

/// <summary>
/// The machine's own screen, mouse and keyboard in graphics mode, shared by whatever has the
/// screen: the desktop, or the login window before anyone logged in.
/// </summary>
/// <remarks>
/// While <see cref="Lock"/> holds a message (a remote desktop viewer owns the desktop), the
/// screen shows only that message and local input is read and dropped, so whoever sits at the
/// machine can neither see nor use the remote user's desktop.
/// </remarks>
public static class LocalDisplay
{
    private static CanvasRenderSource? canvas;

    private static Point lastMousePosition = Point.Empty;
    private static bool lastLeft, lastRight, lastMiddle;

    /// <summary>Gets the message the local screen shows instead of the windows, or null.</summary>
    public static string? LockMessage => canvas?.LockMessage;

    /// <summary>Starts drawing the windows on the local screen; drawing starts once and stays on.</summary>
    public static void Start()
    {
        if (canvas is null)
        {
            canvas = new CanvasRenderSource();
            WindowManager.AddRenderSource(canvas);
        }

        WindowManager.InvalidateAll();
    }

    /// <summary>Shows <paramref name="message"/> on the local screen instead of the windows and ignores local input; null shows the windows again.</summary>
    public static void Lock(string? message)
    {
        if (canvas is not null)
        {
            canvas.LockMessage = message;
        }
    }

    /// <summary>
    /// Hands the local mouse and keyboard to the window manager. Called every tick by whatever has
    /// the screen; while the screen is locked, input is read and dropped.
    /// </summary>
    public static void PollInput()
    {
        bool locked = LockMessage is not null;
        Point position = new(MouseManager.X, MouseManager.Y);

        if (position != lastMousePosition)
        {
            Send(MouseEvent.Move(position.X, position.Y));
            lastMousePosition = position;
        }

        PollButton(MouseManager.LeftButton, ref lastLeft, MouseButton.Left);
        PollButton(MouseManager.RightButton, ref lastRight, MouseButton.Right);
        PollButton(MouseManager.MiddleButton, ref lastMiddle, MouseButton.Middle);

        int scroll = MouseManager.ScrollDelta;
        if (scroll != 0)
        {
            MouseManager.ResetScrollDelta();
            Send(MouseEvent.Wheel(position.X, position.Y, scroll));
        }

        while (KeyboardManager.TryReadKey(out KeyEvent? keyEvent) && keyEvent is not null)
        {
            if (!locked)
            {
                WindowManager.EnqueueKeyEvent(keyEvent);
            }
        }

        void PollButton(bool pressed, ref bool last, MouseButton button)
        {
            if (pressed != last)
            {
                Send(pressed ? MouseEvent.ButtonDown(position.X, position.Y, button) : MouseEvent.ButtonUp(position.X, position.Y, button));
                last = pressed;
            }
        }

        void Send(MouseEvent mouseEvent)
        {
            if (!locked)
            {
                WindowManager.EnqueueMouseEvent(mouseEvent);
            }
        }
    }
}
