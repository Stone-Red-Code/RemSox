using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;
using RemSox.Plugs;

namespace RemSox.Cryptography;

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

        if (!KeyboardManager.KeyAvailable) return;

        KeyEvent keyEvent = KeyboardManager.Peek();

        RandomNumberGeneratorImplementationImpl.AddKeyboardEntropy((uint)keyEvent.Key, (uint)keyEvent.Modifiers, keyEvent.KeyChar);
    }
}
