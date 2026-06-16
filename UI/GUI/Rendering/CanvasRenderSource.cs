using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Mouse;

using RemSox.UI.GUI.UIEelements;
using RemSox.Utils;

using System.Drawing;

namespace RemSox.UI.GUI.Rendering;

public sealed class CanvasRenderSource : IRenderSource
{
    private static readonly Dictionary<int, Canvas> windowCanvases = [];
    private static readonly Dictionary<int, Point> windowPositions = [];
    private static readonly Dictionary<int, int> windowZIndices = [];

    // Sorted list keeps windows in Z-order without re-sorting.
    // Key = (zIndex << 32 | windowId) so equal Z stays insertion-stable.
    private static readonly SortedList<long, int> zOrderedWindows = [];

    // Accumulated drawing primitives per window (in draw order).
    private static readonly Dictionary<int, List<(int ElementId, RenderCommand Command)>> windowPrimitives = [];

    private static readonly HashSet<int> dirtyWindows = [];
    private static bool isPositionDirty = true;
    private static Point lastPointerPosition = new(-1, -1);

    private static readonly Lock renderLock = new();

    public void Render(IEnumerable<RenderCommand> commands)
    {
        lock (renderLock)
        {
            foreach (RenderCommand command in commands)
            {
                switch (command.Type)
                {
                    case RenderCommandType.CreateWindow:
                        CreateOrUpdateWindow(command);
                        break;

                    case RenderCommandType.DestroyWindow:
                        RemoveWindow(command.WindowId);
                        break;

                    case RenderCommandType.MoveWindow:
                        if (windowCanvases.ContainsKey(command.WindowId))
                        {
                            windowPositions[command.WindowId] = command.Position;
                            isPositionDirty = true;
                        }
                        break;

                    case RenderCommandType.RemovePrimitives:
                        RemovePrimitives(command.WindowId, command.ElementId);
                        break;

                    default:
                        if (windowCanvases.ContainsKey(command.WindowId))
                        {
                            UpsertPrimitive(command);
                        }
                        break;
                }
            }
        }
    }

    public void Composite()
    {
        Canvas screenCanvas = FullScreenCanvas.GetFullScreenCanvas();
        Point pointerPosition = new(MouseManager.X, MouseManager.Y);

        lock (renderLock)
        {
            bool pointerMoved = pointerPosition != lastPointerPosition;
            bool hasDirty = dirtyWindows.Count > 0;

            if (!hasDirty && !isPositionDirty && !pointerMoved)
            {
                return;
            }

            // Redraw dirty windows from accumulated primitives
            if (hasDirty)
            {
                foreach (int winId in dirtyWindows)
                {
                    if (!windowCanvases.TryGetValue(winId, out Canvas? canvas))
                    {
                        continue;
                    }

                    canvas.Clear(Color.Black);

                    if (windowPrimitives.TryGetValue(winId, out var primitives))
                    {
                        foreach (var (_, cmd) in primitives)
                        {
                            DrawPrimitive(canvas, cmd);
                        }
                    }
                }
                dirtyWindows.Clear();
            }

            // Draw desktop background
            int w = (int)screenCanvas.Mode.Width;
            int h = (int)screenCanvas.Mode.Height;
            screenCanvas.Clear(Color.FromArgb(45, 45, 48));

            // Subtle horizontal gradient effect (4 bands)
            Color[] bands = [
                Color.FromArgb(30, 30, 35),
                Color.FromArgb(45, 45, 48),
                Color.FromArgb(60, 60, 65),
                Color.FromArgb(45, 45, 48),
            ];
            int bandH = h / bands.Length;
            for (int i = 0; i < bands.Length; i++)
            {
                screenCanvas.DrawFilledRectangle(bands[i], 0, i * bandH, w, bandH + 1);
            }

            // Composite all windows to screen
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
            isPositionDirty = false;
        }
    }

    // --- Accumulated state management ---

    private static void CreateOrUpdateWindow(RenderCommand cmd)
    {
        int id = cmd.WindowId;
        Size size = Get(cmd.Properties, "Size", new Size(160, 120));
        int zIndex = Get(cmd.Properties, "ZIndex", 0);

        if (!windowCanvases.TryGetValue(id, out Canvas? existing) ||
            existing.Mode.Width != size.Width ||
            existing.Mode.Height != size.Height)
        {
            windowCanvases[id] = new Canvas(size.Width, size.Height);
        }

        windowPositions[id] = cmd.Position;

        if (windowZIndices.TryGetValue(id, out int oldZ))
        {
            _ = zOrderedWindows.Remove(ZKey(oldZ, id));
        }
        windowZIndices[id] = zIndex;
        zOrderedWindows[ZKey(zIndex, id)] = id;

        if (!windowPrimitives.ContainsKey(id))
        {
            windowPrimitives[id] = [];
        }

        _ = dirtyWindows.Add(id);
        isPositionDirty = true;
    }

    private static void RemoveWindow(int windowId)
    {
        _ = windowCanvases.Remove(windowId);
        _ = windowPositions.Remove(windowId);
        _ = windowPrimitives.Remove(windowId);

        if (windowZIndices.TryGetValue(windowId, out int z))
        {
            _ = zOrderedWindows.Remove(ZKey(z, windowId));
            _ = windowZIndices.Remove(windowId);
        }

        isPositionDirty = true;
    }

    private static void UpsertPrimitive(RenderCommand cmd)
    {
        if (!windowPrimitives.TryGetValue(cmd.WindowId, out var list))
        {
            list = [];
            windowPrimitives[cmd.WindowId] = list;
        }

        int idx = list.FindIndex(p => p.ElementId == cmd.ElementId);
        if (idx >= 0)
        {
            list[idx] = (cmd.ElementId, cmd);
        }
        else
        {
            list.Add((cmd.ElementId, cmd));
        }

        _ = dirtyWindows.Add(cmd.WindowId);
    }

    private static void RemovePrimitives(int windowId, int baseElementId)
    {
        if (!windowPrimitives.TryGetValue(windowId, out var list))
        {
            return;
        }

        list.RemoveAll(p => p.ElementId >= 0
            ? (p.ElementId >> UIElement.PrimitiveIdShift) == baseElementId
            : p.ElementId == baseElementId);

        _ = dirtyWindows.Add(windowId);
    }

    // --- Primitive drawing ---

    private static void DrawPrimitive(Canvas canvas, RenderCommand cmd)
    {
        switch (cmd.Type)
        {
            case RenderCommandType.DrawFilledRect:
                DrawFilledRect(canvas, cmd);
                break;
            case RenderCommandType.DrawRectBorder:
                DrawRectBorder(canvas, cmd);
                break;
            case RenderCommandType.DrawFilledCircle:
                DrawFilledCircle(canvas, cmd);
                break;
            case RenderCommandType.DrawCircle:
                DrawCircle(canvas, cmd);
                break;
            case RenderCommandType.DrawPoint:
                DrawPoint(canvas, cmd);
                break;
            case RenderCommandType.DrawText:
                DrawText(canvas, cmd);
                break;
            case RenderCommandType.DrawLine:
                DrawLine(canvas, cmd);
                break;
        }
    }

    // Builds a stable sort key from Z-index and window ID.
    private static long ZKey(int z, int id)
    {
        return ((long)z << 32) | (uint)id;
    }

    private static T Get<T>(IReadOnlyDictionary<string, object?> props, string key, T fallback)
    {
        return props.TryGetValue(key, out object? raw) && raw is T value ? value : fallback;
    }

    private static void DrawFilledRect(Canvas canvas, RenderCommand cmd)
    {
        Color color = Get(cmd.Properties, "Color", Color.White);
        Size size = Get(cmd.Properties, "Size", new Size(10, 10));
        canvas.DrawFilledRectangle(color, cmd.Position.X, cmd.Position.Y, size.Width, size.Height);
    }

    private static void DrawRectBorder(Canvas canvas, RenderCommand cmd)
    {
        Color color = Get(cmd.Properties, "Color", Color.White);
        Size size = Get(cmd.Properties, "Size", new Size(10, 10));
        canvas.DrawRectangle(color, cmd.Position.X, cmd.Position.Y, size.Width, size.Height);
    }

    private static void DrawFilledCircle(Canvas canvas, RenderCommand cmd)
    {
        Color color = Get(cmd.Properties, "Color", Color.White);
        int radius = Get(cmd.Properties, "Radius", 10);
        canvas.DrawFilledCircle(color, cmd.Position.X + radius, cmd.Position.Y + radius, radius);
    }

    private static void DrawCircle(Canvas canvas, RenderCommand cmd)
    {
        Color color = Get(cmd.Properties, "Color", Color.White);
        int radius = Get(cmd.Properties, "Radius", 10);
        canvas.DrawCircle(color, cmd.Position.X + radius, cmd.Position.Y + radius, radius);
    }

    private static void DrawPoint(Canvas canvas, RenderCommand cmd)
    {
        Color color = Get(cmd.Properties, "Color", Color.White);
        canvas.DrawPoint(color, cmd.Position.X, cmd.Position.Y);
    }

    private static void DrawText(Canvas canvas, RenderCommand cmd)
    {
        Color color = Get(cmd.Properties, "Color", Color.White);
        string content = Get(cmd.Properties, "Content", string.Empty);
        int fontSize = Get(cmd.Properties, "FontSize", 12);
        int maxWidth = Get(cmd.Properties, "MaxWidth", int.MaxValue);

        if (!string.IsNullOrEmpty(content))
        {
            canvas.DrawStringHeight(content, PCScreenFont.DefaultFont, color, cmd.Position.X, cmd.Position.Y, fontSize, maxWidth);
        }
    }

    private static void DrawLine(Canvas canvas, RenderCommand cmd)
    {
        Color color = Get(cmd.Properties, "Color", Color.White);
        Point end = Get(cmd.Properties, "EndPosition", cmd.Position);
        canvas.DrawLine(color, cmd.Position.X, cmd.Position.Y, end.X, end.Y);
    }
}