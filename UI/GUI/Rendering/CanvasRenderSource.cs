using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace RemSox.UI.GUI.Rendering;

public sealed class CanvasRenderSource : IRenderSource
{
    public void Render(IEnumerable<RenderCommand> commands)
    {
        Canvas canvas = FullScreenCanvas.GetFullScreenCanvas();

        foreach (RenderCommand command in commands)
        {
            if (command.ElementType == "Window")
            {
                RenderWindow(canvas, command);
                continue;
            }

            if (command.ElementType == "Circle")
            {
                RenderCircle(canvas, command);
            }
        }
    }

    private static void RenderWindow(Canvas canvas, RenderCommand command)
    {
        int x = command.Position.X;
        int y = command.Position.Y;

        Size size = command.Properties.TryGetValue(nameof(Size), out object? rawSize) && rawSize is Size windowSize
            ? windowSize
            : new Size(160, 120);

        bool isFocused = command.Properties.TryGetValue(nameof(Windows.Window.IsFocused), out object? rawFocused) && rawFocused is bool focused && focused;

        Color borderColor = isFocused ? Color.White : Color.DarkGray;
        Color bodyColor = Color.FromArgb(32, 32, 32);
        Color titleColor = isFocused ? Color.FromArgb(0, 120, 215) : Color.FromArgb(80, 80, 80);

        canvas.DrawFilledRectangle(bodyColor, x, y, size.Width, size.Height);
        canvas.DrawFilledRectangle(titleColor, x, y, size.Width, 18);
        canvas.DrawRectangle(borderColor, x, y, size.Width, size.Height);
    }

    private static void RenderCircle(Canvas canvas, RenderCommand command)
    {
        Color color = command.Properties.TryGetValue(nameof(Color), out object? rawColor) && rawColor is Color circleColor
            ? circleColor
            : Color.White;

        int radius = command.Properties.TryGetValue(nameof(Radius), out object? rawRadius) && rawRadius is int circleRadius
            ? circleRadius
            : 10;

        int centerX = command.Position.X + radius;
        int centerY = command.Position.Y + radius;

        canvas.DrawFilledCircle(color, centerX, centerY, radius);
    }

    private static string Radius => nameof(Radius);
}