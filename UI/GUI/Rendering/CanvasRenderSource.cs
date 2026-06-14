using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

using RemSox.Utils;

using System.Drawing;

namespace RemSox.UI.GUI.Rendering;

public sealed class CanvasRenderSource : IRenderSource
{
    private static readonly Dictionary<int, Canvas> windowCanvases = [];
    private static readonly Dictionary<int, Point> windowPositions = [];
    private static readonly Dictionary<int, int> windowZIndices = [];

    // Sorted list keeps windows in Z-order without re-sorting.
    // Key = (zIndex << 16 | windowId) so equal Z stays insertion-stable.
    private static readonly SortedList<long, int> zOrderedWindows = [];

    private static bool isContentDirty = true;   // pixel content changed
    private static bool isPositionDirty = true;   // only layout changed
    private static Point lastPointerPosition = new(-1, -1);

    private static readonly Lock renderLock = new();

    private static readonly Dictionary<string, Action<Canvas, RenderCommand>> elementRenderers = new()
    {
        ["Circle"] = RenderCircle,
        ["Rectangle"] = RenderRectangle,
        ["Text"] = RenderText,
        ["Line"] = RenderLine,
        ["Button"] = RenderButton,
        ["CheckBox"] = RenderCheckBox,
    };

    public void Render(IEnumerable<RenderCommand> commands)
    {
        lock (renderLock)
        {
            foreach (RenderCommand command in commands)
            {
                switch (command.ElementType)
                {
                    case "WindowClose":
                        RemoveWindow(command.WindowId);
                        continue;

                    case "WindowMove":
                        if (windowCanvases.ContainsKey(command.WindowId))
                        {
                            windowPositions[command.WindowId] = command.Position;
                            isPositionDirty = true;
                        }
                        continue;

                    case "Window":
                        ProcessWindowCommand(command);
                        isContentDirty = true;
                        continue;
                }

                if (!windowCanvases.TryGetValue(command.WindowId, out Canvas? canvas))
                {
                    continue;
                }

                if (elementRenderers.TryGetValue(command.ElementType, out Action<Canvas, RenderCommand>? renderer))
                {
                    renderer(canvas, command);
                    isContentDirty = true;
                }
            }
        }
    }

    public static void CompositeAndDisplay(Canvas screenCanvas, Point pointerPosition)
    {
        lock (renderLock)
        {
            bool pointerMoved = pointerPosition != lastPointerPosition;
            if (!isContentDirty && !isPositionDirty && !pointerMoved)
            {
                return;
            }

            screenCanvas.Clear(Color.Black);

            foreach (int windowId in zOrderedWindows.Values)
            {
                if (windowPositions.TryGetValue(windowId, out Point pos) &&
                    windowCanvases.TryGetValue(windowId, out Canvas? wc))
                {
                    screenCanvas.DrawCanvas(wc, pos.X, pos.Y);
                }
            }

            screenCanvas.DrawFilledCircle(Color.White, pointerPosition.X, pointerPosition.Y, 5);
            screenCanvas.Display();

            lastPointerPosition = pointerPosition;
            isContentDirty = false;
            isPositionDirty = false;
        }
    }

    // Helpers

    private static void RemoveWindow(int windowId)
    {
        _ = windowCanvases.Remove(windowId);
        _ = windowPositions.Remove(windowId);

        if (windowZIndices.TryGetValue(windowId, out int z))
        {
            _ = zOrderedWindows.Remove(ZKey(z, windowId));
            _ = windowZIndices.Remove(windowId);
        }

        isPositionDirty = true;
    }

    private static void ProcessWindowCommand(RenderCommand command)
    {
        int id = command.WindowId;

        if (command.Properties.TryGetValue("ZIndex", out object? rawZ) && rawZ is int newZ)
        {
            if (windowZIndices.TryGetValue(id, out int oldZ))
            {
                _ = zOrderedWindows.Remove(ZKey(oldZ, id));
            }

            windowZIndices[id] = newZ;
            zOrderedWindows[ZKey(newZ, id)] = id;
        }
        else if (!windowZIndices.ContainsKey(id))
        {
            windowZIndices[id] = 0;
            zOrderedWindows[ZKey(0, id)] = id;
        }

        Size size = Get(command.Properties, "Size", new Size(160, 120));

        if (!windowCanvases.TryGetValue(id, out Canvas? current) ||
            current.Mode.Width != size.Width ||
            current.Mode.Height != size.Height)
        {
            windowCanvases[id] = new Canvas(size.Width, size.Height);
        }

        windowPositions[id] = command.Position;
        isPositionDirty = true;
        RenderWindow(windowCanvases[id], command);
    }

    // Builds a stable sort key from Z-index and window ID.
    private static long ZKey(int z, int id)
    {
        return ((long)z << 32) | (uint)id;
    }

    // Inline generic property getter — eliminates repeated TryGetValue + pattern-match boilerplate.
    private static T Get<T>(IReadOnlyDictionary<string, object?> props, string key, T fallback)
    {
        return props.TryGetValue(key, out object? raw) && raw is T value ? value : fallback;
    }

    private static void RenderWindow(Canvas canvas, RenderCommand command)
    {
        Size size = Get(command.Properties, "Size", new Size(160, 120));
        bool focused = Get(command.Properties, "IsFocused", false);
        string titleText = Get(command.Properties, "Title", string.Empty);

        Color border = focused ? Color.White : Color.DarkGray;
        Color title = focused ? Color.FromArgb(0, 120, 215) : Color.FromArgb(80, 80, 80);

        canvas.DrawFilledRectangle(Color.FromArgb(32, 32, 32), 0, 0, size.Width, size.Height);
        canvas.DrawFilledRectangle(title, 0, 0, size.Width, 18);
        canvas.DrawStringHeight(titleText, PCScreenFont.DefaultFont, Color.White, 4, 2, 18);
        canvas.DrawRectangle(border, 0, 0, size.Width, size.Height - 1);
    }

    private static void RenderCircle(Canvas canvas, RenderCommand command)
    {
        Color color = Get(command.Properties, "Color", Color.White);
        int radius = Get(command.Properties, "Radius", 10);

        canvas.DrawFilledCircle(color, command.Position.X + radius, command.Position.Y + radius, radius);
    }

    private static void RenderRectangle(Canvas canvas, RenderCommand command)
    {
        Color color = Get(command.Properties, "Color", Color.White);
        Size size = Get(command.Properties, "Size", new Size(10, 10));
        bool isFilled = Get(command.Properties, "IsFilled", false);

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
        Color color = Get(command.Properties, "Color", Color.White);
        string content = Get(command.Properties, "Content", string.Empty);
        int fontSize = Get(command.Properties, "FontSize", 12);

        if (!string.IsNullOrEmpty(content))
        {
            canvas.DrawStringHeight(content, PCScreenFont.DefaultFont, color, command.Position.X, command.Position.Y, fontSize);
        }
    }

    private static void RenderLine(Canvas canvas, RenderCommand command)
    {
        Color color = Get(command.Properties, "Color", Color.White);
        Point end = Get(command.Properties, "EndPosition", command.Position);

        canvas.DrawLine(color, command.Position.X, command.Position.Y, end.X, end.Y);
    }

    private static void RenderButton(Canvas canvas, RenderCommand command)
    {
        Color bg = Get(command.Properties, "BackgroundColor", Color.LightGray);
        Color fg = Get(command.Properties, "TextColor", Color.Black);
        Size size = Get(command.Properties, "Size", new Size(60, 20));
        string text = Get(command.Properties, "Text", string.Empty);

        canvas.DrawFilledRectangle(bg, command.Position.X, command.Position.Y, size.Width, size.Height);
        canvas.DrawRectangle(Color.DarkGray, command.Position.X, command.Position.Y, size.Width, size.Height);

        if (!string.IsNullOrEmpty(text))
        {
            int tx = command.Position.X + (size.Width / 2) - (text.Length * 4);
            int ty = command.Position.Y + (size.Height / 2) - 8;
            canvas.DrawStringHeight(text, PCScreenFont.DefaultFont, fg, tx, ty, size.Height - 8);
        }
    }

    private static void RenderCheckBox(Canvas canvas, RenderCommand command)
    {
        Color bg = Get(command.Properties, "BackgroundColor", Color.LightGray);
        Color fg = Get(command.Properties, "TextColor", Color.White);
        bool isChecked = Get(command.Properties, "IsChecked", false);
        string text = Get(command.Properties, "Text", string.Empty);
        Size size = Get(command.Properties, "Size", new Size(12, 12));

        int boxSize = size.Height;

        canvas.DrawFilledRectangle(bg, command.Position.X, command.Position.Y, boxSize, boxSize);
        canvas.DrawRectangle(Color.DarkGray, command.Position.X, command.Position.Y, boxSize, boxSize);

        if (isChecked)
        {
            canvas.DrawFilledRectangle(Color.Black, command.Position.X + 3, command.Position.Y + 3, boxSize - 6, boxSize - 6);
        }

        if (!string.IsNullOrEmpty(text))
        {
            canvas.DrawStringHeight(text, PCScreenFont.DefaultFont, fg, command.Position.X + boxSize + 5, command.Position.Y - 2, boxSize);
        }
    }
}