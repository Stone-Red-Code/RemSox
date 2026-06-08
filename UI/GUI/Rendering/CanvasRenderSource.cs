using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

namespace RemSox.UI.GUI.Rendering;

public sealed class CanvasRenderSource : IRenderSource
{
    private static readonly Dictionary<int, Canvas> windowCanvases = new();
    private static readonly Dictionary<int, Point> windowPositions = new();
    private static readonly Dictionary<int, int> windowZIndices = new();

    private static bool isDirty = true;
    private static Point lastPointerPosition = new Point(-1, -1);
    private static List<int> orderedWindowsCache = new();
    private static bool isZOrderDirty = true;

    public void Render(IEnumerable<RenderCommand> commands)
    {
        bool changed = false;
        foreach (RenderCommand command in commands)
        {
            changed = true;
            if (command.ElementType == "WindowClose")
            {
                windowCanvases.Remove(command.WindowId);
                windowPositions.Remove(command.WindowId);
                windowZIndices.Remove(command.WindowId);
                isZOrderDirty = true;
                continue;
            }

            if (command.ElementType == "Window" || command.ElementType == "WindowMove")
            {
                if (command.Properties.TryGetValue("ZIndex", out object? rawZIndex) && rawZIndex is int z)
                {
                    windowZIndices[command.WindowId] = z;
                    isZOrderDirty = true;
                }
            }

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

            if (command.ElementType == "Rectangle")
            {
                RenderRectangle(windowCanvas, command);
            }

            if (command.ElementType == "Text")
            {
                RenderText(windowCanvas, command);
            }
        }

        if (changed)
        {
            isDirty = true;
        }
    }

    public static void CompositeAndDisplay(Canvas screenCanvas, Point pointerPosition)
    {
        if (!isDirty && pointerPosition == lastPointerPosition)
        {
            return;
        }

        if (isZOrderDirty)
        {
            orderedWindowsCache = windowPositions.Keys.OrderBy(id => windowZIndices.TryGetValue(id, out int z) ? z : 0).ToList();
            isZOrderDirty = false;
        }

        //screenCanvas.Clear(Color.Black);

        foreach (var windowId in orderedWindowsCache)
        {
            if (windowPositions.TryGetValue(windowId, out Point position) && windowCanvases.TryGetValue(windowId, out Canvas? windowCanvas))
            {
                screenCanvas.DrawCanvas(windowCanvas, position.X, position.Y);
            }
        }

        screenCanvas.DrawFilledCircle(Color.White, pointerPosition.X, pointerPosition.Y, 5);
        screenCanvas.Display();

        lastPointerPosition = pointerPosition;
        isDirty = false;
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

    private static void RenderRectangle(Canvas canvas, RenderCommand command)
    {
        Color color = command.Properties.TryGetValue("Color", out object? rawColor) && rawColor is Color rectColor
            ? rectColor
            : Color.White;

        Size size = command.Properties.TryGetValue("Size", out object? rawSize) && rawSize is Size rectSize
            ? rectSize
            : new Size(10, 10);

        bool isFilled = command.Properties.TryGetValue("IsFilled", out object? rawFilled) && rawFilled is bool filled && filled;

        if (isFilled)
        {
            canvas.DrawFilledRectangle(color, command.Position.X, command.Position.Y, size.Width, size.Height);
        }
        else
        {
            canvas.DrawRectangle(color, command.Position.X, command.Position.Y, size.Width, size.Height);
        }
    }

    private static void RenderText(Canvas canvas, RenderCommand command)
    {
        Color color = command.Properties.TryGetValue("Color", out object? rawColor) && rawColor is Color textColor
            ? textColor
            : Color.White;

        string content = command.Properties.TryGetValue("Content", out object? rawContent) && rawContent is string textContent
            ? textContent
            : string.Empty;

        if (!string.IsNullOrEmpty(content))
        {
            canvas.DrawString(content, Cosmos.Kernel.System.Graphics.Fonts.PCScreenFont.DefaultFont, color, command.Position.X, command.Position.Y);
        }
    }
}