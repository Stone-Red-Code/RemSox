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
    private static readonly object renderLock = new object();

    public void Render(IEnumerable<RenderCommand> commands)
    {
        lock (renderLock)
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

                if (command.ElementType == "Line")
                {
                    RenderLine(windowCanvas, command);
                }

                if (command.ElementType == "Button")
                {
                    RenderButton(windowCanvas, command);
                }

                if (command.ElementType == "CheckBox")
                {
                    RenderCheckBox(windowCanvas, command);
                }
            }

            if (changed)
            {
                isDirty = true;
            }
        }
    }

    public static void CompositeAndDisplay(Canvas screenCanvas, Point pointerPosition)
    {
        lock (renderLock)
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

            screenCanvas.Clear(Color.Black);

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
            canvas.DrawString(content, PCScreenFont.DefaultFont, color, command.Position.X, command.Position.Y);
        }
    }

    private static void RenderLine(Canvas canvas, RenderCommand command)
    {
        Color color = command.Properties.TryGetValue("Color", out object? rawColor) && rawColor is Color lineColor
            ? lineColor
            : Color.White;

        Point endPosition = command.Properties.TryGetValue("EndPosition", out object? rawEnd) && rawEnd is Point endPoint
            ? endPoint
            : command.Position;

        canvas.DrawLine(color, command.Position.X, command.Position.Y, endPosition.X, endPosition.Y);
    }

    private static void RenderButton(Canvas canvas, RenderCommand command)
    {
        Color bgColor = command.Properties.TryGetValue("BackgroundColor", out object? rawBgColor) && rawBgColor is Color c1 ? c1 : Color.LightGray;
        Color textColor = command.Properties.TryGetValue("TextColor", out object? rawTextColor) && rawTextColor is Color c2 ? c2 : Color.Black;
        Size size = command.Properties.TryGetValue("Size", out object? rawSize) && rawSize is Size s ? s : new Size(60, 20);
        string text = command.Properties.TryGetValue("Text", out object? rawText) && rawText is string t ? t : string.Empty;

        canvas.DrawFilledRectangle(bgColor, command.Position.X, command.Position.Y, size.Width, size.Height);
        canvas.DrawRectangle(Color.DarkGray, command.Position.X, command.Position.Y, size.Width, size.Height);

        if (!string.IsNullOrEmpty(text))
        {
            int textX = command.Position.X + (size.Width / 2) - (text.Length * 8 / 2);
            int textY = command.Position.Y + (size.Height / 2) - 8;
            canvas.DrawString(text, PCScreenFont.DefaultFont, textColor, textX, textY);
        }
    }

    private static void RenderCheckBox(Canvas canvas, RenderCommand command)
    {
        Color bgColor = command.Properties.TryGetValue("BackgroundColor", out object? rawBgColor) && rawBgColor is Color c1 ? c1 : Color.LightGray;
        Color textColor = command.Properties.TryGetValue("TextColor", out object? rawTextColor) && rawTextColor is Color c2 ? c2 : Color.White;
        bool isChecked = command.Properties.TryGetValue("IsChecked", out object? rawChecked) && rawChecked is bool chk ? chk : false;
        string text = command.Properties.TryGetValue("Text", out object? rawText) && rawText is string t ? t : string.Empty;

        int boxSize = 12;

        canvas.DrawFilledRectangle(bgColor, command.Position.X, command.Position.Y, boxSize, boxSize);
        canvas.DrawRectangle(Color.DarkGray, command.Position.X, command.Position.Y, boxSize, boxSize);

        if (isChecked)
        {
            canvas.DrawFilledRectangle(Color.Black, command.Position.X + 3, command.Position.Y + 3, boxSize - 6, boxSize - 6);
        }

        if (!string.IsNullOrEmpty(text))
        {
            canvas.DrawString(text, PCScreenFont.DefaultFont, textColor, command.Position.X + boxSize + 5, command.Position.Y - 2);
        }
    }
}