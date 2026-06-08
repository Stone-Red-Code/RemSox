using System;
using System.Drawing;
using System.Collections.Generic;
using Cosmos.Kernel.System.Graphics;

namespace RemSox.UI.GUI.Rendering;

public sealed class CanvasRenderSource : IRenderSource
{
    private static readonly Dictionary<int, Canvas> windowCanvases = new();
    private static readonly Dictionary<int, Point> windowPositions = new();

    public void Render(IEnumerable<RenderCommand> commands)
    {
        foreach (RenderCommand command in commands)
        {
            if (command.ElementType == "Window")
            {
                Size size = command.Properties.TryGetValue("Size", out object? rawSize) && rawSize is Size windowSize
                    ? windowSize
                    : new Size(160, 120);

                if (!windowCanvases.TryGetValue(command.WindowId, out Canvas? currentCanvas) || 
                    currentCanvas.Mode.Width != size.Width || 
                    currentCanvas.Mode.Height != size.Height)
                {
                    windowCanvases[command.WindowId] = new Canvas(size.Width, size.Height);
                }
            }
            
            if (!windowCanvases.ContainsKey(command.WindowId))
            {
                windowCanvases[command.WindowId] = new Canvas(160, 120);
            }

            Canvas windowCanvas = windowCanvases[command.WindowId];

            if (command.ElementType == "Window")
            {
                RenderWindow(windowCanvas, command);
                windowPositions[command.WindowId] = command.Position;
                continue;
            }

            if (command.ElementType == "WindowMove")
            {
                windowPositions[command.WindowId] = command.Position;
                continue;
            }

            if (command.ElementType == "Circle")
            {
                RenderCircle(windowCanvas, command);
            }
        }
    }

    public static void CompositeAndDisplay(Canvas screenCanvas, Point pointerPosition)
    {
        screenCanvas.Clear(Color.Black);

        foreach (var kvp in windowPositions)
        {
            int windowId = kvp.Key;
            Point position = kvp.Value;
            if (windowCanvases.TryGetValue(windowId, out Canvas? windowCanvas))
            {
                screenCanvas.DrawCanvas(windowCanvas, position.X, position.Y);
            }
        }

        screenCanvas.DrawFilledCircle(Color.White, pointerPosition.X, pointerPosition.Y, 5);
        screenCanvas.Display();
    }

    private static void RenderWindow(Canvas canvas, RenderCommand command)
    {
        Size size = command.Properties.TryGetValue("Size", out object? rawSize) && rawSize is Size windowSize
            ? windowSize
            : new Size(160, 120);

        bool isFocused = command.Properties.TryGetValue("IsFocused", out object? rawFocused) && rawFocused is bool focused && focused;

        Color borderColor = isFocused ? Color.White : Color.DarkGray;
        Color bodyColor = Color.FromArgb(32, 32, 32);
        Color titleColor = isFocused ? Color.FromArgb(0, 120, 215) : Color.FromArgb(80, 80, 80);

        canvas.DrawFilledRectangle(bodyColor, 0, 0, size.Width, size.Height);
        canvas.DrawFilledRectangle(titleColor, 0, 0, size.Width, 18);
        canvas.DrawRectangle(borderColor, 0, 0, size.Width, size.Height);
    }

    private static void RenderCircle(Canvas canvas, RenderCommand command)
    {
        Color color = command.Properties.TryGetValue("Color", out object? rawColor) && rawColor is Color circleColor
            ? circleColor
            : Color.White;

        int radius = command.Properties.TryGetValue("Radius", out object? rawRadius) && rawRadius is int circleRadius
            ? circleRadius
            : 10;

        int centerX = command.Position.X + radius;
        int centerY = command.Position.Y + radius;

        canvas.DrawFilledCircle(color, centerX, centerY, radius);
    }
}