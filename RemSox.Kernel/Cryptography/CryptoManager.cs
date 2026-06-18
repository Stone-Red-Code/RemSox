using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;

using RemSox.Kernel.Plugs;

using System.Drawing;

namespace RemSox.Kernel.Cryptography;

public static class CryptoManager
{
    private static Point lastMousePosition = new Point(0, 0);

    public static void Update()
    {
        int x = MouseManager.X;
        int y = MouseManager.Y;
        int dx = x - lastMousePosition.X;
        int dy = y - lastMousePosition.Y;
        int dz = MouseManager.ScrollDelta;

        lastMousePosition = new Point(x, y);

        RandomNumberGeneratorImplementationImpl.AddMouseEntropy(dx, dy, dz, x, y);

        if (!KeyboardManager.KeyAvailable)
        {
            return;
        }

        KeyEvent keyEvent = KeyboardManager.Peek();

        RandomNumberGeneratorImplementationImpl.AddKeyboardEntropy((uint)keyEvent.Key, (uint)keyEvent.Modifiers, keyEvent.KeyChar);
    }
}
