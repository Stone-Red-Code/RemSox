using RemSox.Shared.UI.GUI.Rendering;
using Cosmos.Kernel.HAL.Devices.Display;
using Cosmos.Kernel.System.Graphics;

using RemSox.Kernel.UI.GUI.UIEelements;
using RemSox.Kernel.Utils;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.Rendering;

public sealed class CanvasRenderSource : IRenderSource
{
    private readonly Dictionary<int, Canvas> windowCanvases = [];
    private readonly Dictionary<int, Point> windowPositions = [];
    private readonly Dictionary<int, int> windowZIndices = [];
    private readonly Dictionary<int, byte> windowOpacities = [];

    // Sorted list keeps windows in Z-order without re-sorting.
    // Key = (zIndex << 32 | windowId) so equal Z stays insertion-stable.
    private readonly SortedList<long, int> zOrderedWindows = [];

    // Accumulated drawing primitives per window (in draw order).
    private readonly Dictionary<int, List<(int ElementId, RenderCommand Command)>> windowPrimitives = [];

    private readonly HashSet<int> dirtyWindows = [];

    // Whether the scene (background and windows, without the cursor) must be composited again.
    // A cursor move alone does not: the cursor is drawn over the composited scene.
    private bool isPositionDirty = true;
    private Point lastPointerPosition = new(-1, -1);

    // The scene's pixels under the cursor, saved before the cursor is drawn over them, so a
    // cursor-only frame puts them back instead of compositing the whole scene again.
    private const int CursorRadius = 5;
    private const int CursorSize = (CursorRadius * 2) + 1;
    private readonly int[] underCursor = new int[CursorSize * CursorSize];
    private Rectangle underCursorArea = Rectangle.Empty;

    // The display's own cursor, when its driver has one (VMware SVGA, Intel, AMD): the display
    // composes it, so a cursor move costs neither drawing nor a Display() copy.
    private HardwareCursor? hardwareCursor;
    private bool hardwareCursorProbed;

    private Point cursorPosition;
    private int screenWidth, screenHeight;

    private readonly Lock renderLock = new();

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

                    case RenderCommandType.SetWindowOpacity:
                        if (windowCanvases.ContainsKey(command.WindowId))
                        {
                            windowOpacities[command.WindowId] = Get(command.Properties, "Opacity", (byte)255);
                            isPositionDirty = true;
                        }
                        break;

                    case RenderCommandType.SetCursor:
                        cursorPosition = command.Position;
                        break;

                    case RenderCommandType.ScreenInfo:
                        screenWidth = Get(command.Properties, "Width", 640);
                        screenHeight = Get(command.Properties, "Height", 480);
                        isPositionDirty = true;
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
        Canvas screenCanvas = Canvas.GetFullScreen();
        Point pointerPosition = cursorPosition;

        lock (renderLock)
        {
            bool pointerMoved = pointerPosition != lastPointerPosition;
            bool sceneDirty = dirtyWindows.Count > 0 || isPositionDirty;

            if (!sceneDirty && !pointerMoved)
            {
                return;
            }

            if (!hardwareCursorProbed)
            {
                hardwareCursorProbed = true;
                hardwareCursor = HardwareCursor.TryCreate(CursorRadius);
            }

            if (hardwareCursor is not null)
            {
                if (sceneDirty)
                {
                    RedrawDirtyWindows();
                    CompositeScene(screenCanvas);
                    isPositionDirty = false;
                    screenCanvas.Display();
                }

                if (pointerMoved)
                {
                    hardwareCursor.MoveTo(pointerPosition);
                }

                lastPointerPosition = pointerPosition;
                return;
            }

            int[]? screen = screenCanvas.GetBuffer();

            if (sceneDirty || screen is null)
            {
                RedrawDirtyWindows();
                CompositeScene(screenCanvas);
                underCursorArea = Rectangle.Empty;
                isPositionDirty = false;
            }
            else
            {
                // Only the cursor moved: put back the scene under its old position.
                RestoreUnderCursor(screenCanvas, screen);
            }

            if (screen is not null)
            {
                SaveUnderCursor(screenCanvas, screen, pointerPosition);
            }

            screenCanvas.DrawFilledCircle(Color.White, pointerPosition.X, pointerPosition.Y, CursorRadius);
            screenCanvas.Display();

            lastPointerPosition = pointerPosition;
        }
    }

    /// <summary>Redraws the windows whose primitives changed from their accumulated primitives.</summary>
    private void RedrawDirtyWindows()
    {
        foreach (int winId in dirtyWindows)
        {
            if (!windowCanvases.TryGetValue(winId, out Canvas? canvas))
            {
                continue;
            }

            canvas.Clear(Color.Black);

            if (windowPrimitives.TryGetValue(winId, out List<(int ElementId, RenderCommand Command)>? primitives))
            {
                foreach ((int _, RenderCommand? cmd) in primitives)
                {
                    DrawPrimitive(canvas, cmd);
                }
            }
        }

        dirtyWindows.Clear();
    }

    private static readonly Color[] backgroundBands =
    [
        Color.FromArgb(30, 30, 35),
        Color.FromArgb(45, 45, 48),
        Color.FromArgb(60, 60, 65),
        Color.FromArgb(45, 45, 48),
    ];

    /// <summary>Draws the desktop background and every window, in Z order.</summary>
    private void CompositeScene(Canvas screenCanvas)
    {
        int w = (int)screenCanvas.Mode.Width;
        int h = (int)screenCanvas.Mode.Height;
        screenCanvas.Clear(Color.FromArgb(45, 45, 48));

        // Subtle horizontal gradient effect (4 bands)
        int bandH = h / backgroundBands.Length;
        for (int i = 0; i < backgroundBands.Length; i++)
        {
            screenCanvas.DrawFilledRectangle(backgroundBands[i], 0, i * bandH, w, bandH + 1);
        }

        foreach (int windowId in zOrderedWindows.Values)
        {
            if (!windowPositions.TryGetValue(windowId, out Point pos) ||
                !windowCanvases.TryGetValue(windowId, out Canvas? wc))
            {
                continue;
            }

            byte opacity = windowOpacities.GetValueOrDefault(windowId, (byte)255);
            int[]? pixels = wc.GetBuffer();

            // A window canvas is cleared to opaque black before its primitives are drawn, so its
            // pixels are opaque: copy them row by row instead of blending each one. Only a window
            // composited with less than full opacity (one being dragged) needs blending.
            if (opacity == 255 && pixels is not null)
            {
                screenCanvas.DrawArray(pixels, pos.X, pos.Y, wc.Width, wc.Height);
            }
            else if (pixels is not null)
            {
                BlendOpaque(screenCanvas, pixels, pos.X, pos.Y, wc.Width, wc.Height, opacity);
            }
            else
            {
                screenCanvas.DrawCanvas(wc, pos.X, pos.Y, opacity);
            }
        }
    }

    /// <summary>
    /// Lays opaque pixels over the screen at a constant <paramref name="opacity"/>.
    /// </summary>
    /// <remarks>
    /// Canvas.DrawCanvas handles any mix of source and destination alpha per pixel; window pixels
    /// are always opaque and the opacity is the same for all of them, so each channel is a plain
    /// weighted average. Red and blue are weighted together in one multiply, green in another.
    /// </remarks>
    private static void BlendOpaque(Canvas screenCanvas, int[] pixels, int x, int y, int width, int height, byte opacity)
    {
        int[]? screen = screenCanvas.GetBuffer();
        Rectangle area = Rectangle.Intersect(new Rectangle(x, y, width, height), new Rectangle(0, 0, screenCanvas.Width, screenCanvas.Height));
        if (screen is null || area.IsEmpty)
        {
            return;
        }

        uint weight = opacity + 1u;          // 1..256, so that 255 copies the source exactly
        uint inverse = 256u - weight;

        for (int row = 0; row < area.Height; row++)
        {
            Span<int> source = pixels.AsSpan(((area.Y - y + row) * width) + (area.X - x), area.Width);
            Span<int> target = screen.AsSpan(((area.Y + row) * screenCanvas.Width) + area.X, area.Width);

            for (int i = 0; i < source.Length; i++)
            {
                uint s = (uint)source[i];
                uint d = (uint)target[i];
                uint redBlue = ((((s & 0xFF00FFu) * weight) + ((d & 0xFF00FFu) * inverse)) >> 8) & 0xFF00FFu;
                uint green = ((((s & 0x00FF00u) * weight) + ((d & 0x00FF00u) * inverse)) >> 8) & 0x00FF00u;
                target[i] = (int)(0xFF000000u | redBlue | green);
            }
        }
    }

    /// <summary>The part of the cursor's square around <paramref name="center"/> that is on the screen.</summary>
    private static Rectangle GetCursorArea(Canvas screenCanvas, Point center)
    {
        Rectangle area = new(center.X - CursorRadius, center.Y - CursorRadius, CursorSize, CursorSize);
        area.Intersect(new Rectangle(0, 0, screenCanvas.Width, screenCanvas.Height));
        return area;
    }

    private void SaveUnderCursor(Canvas screenCanvas, int[] screen, Point center)
    {
        underCursorArea = GetCursorArea(screenCanvas, center);

        for (int row = 0; row < underCursorArea.Height; row++)
        {
            Array.Copy(screen, ((underCursorArea.Y + row) * screenCanvas.Width) + underCursorArea.X, underCursor, row * CursorSize, underCursorArea.Width);
        }
    }

    private void RestoreUnderCursor(Canvas screenCanvas, int[] screen)
    {
        for (int row = 0; row < underCursorArea.Height; row++)
        {
            Array.Copy(underCursor, row * CursorSize, screen, ((underCursorArea.Y + row) * screenCanvas.Width) + underCursorArea.X, underCursorArea.Width);
        }
    }

    /// <summary>The display's hardware cursor, showing the same white dot the software cursor draws.</summary>
#pragma warning disable COSMOS0003 // IHardwareCursor is an experimental driver kit seam.
    private sealed class HardwareCursor(IHardwareCursor cursor)
    {
        // Sizes tried for the cursor image: the dot alone, then the fixed sizes cursor planes take.
        private static readonly int[] ImageSizes = [CursorSize, 32, 64];

        /// <summary>Returns the primary display's hardware cursor showing the dot, or null when it has none.</summary>
        public static HardwareCursor? TryCreate(int radius)
        {
            DisplayDevice? display = DisplayManager.IsEnabled ? DisplayManager.Primary : null;
            if (display is null || !display.TryGetFacet(out IHardwareCursor? cursor) || cursor is null)
            {
                return null;
            }

            foreach (int size in ImageSizes)
            {
                if (cursor.TryDefine(radius, radius, size, size, CreateDot(radius, size)))
                {
                    Cosmos.Kernel.System.Diagnostics.Log.WriteString($"[RemSox] Hardware cursor of {display.Name} ({display.DriverName}), {size}x{size}" + System.Environment.NewLine);
                    return new HardwareCursor(cursor);
                }
            }

            Cosmos.Kernel.System.Diagnostics.Log.WriteString($"[RemSox] {display.Name} refused the cursor image; drawing the cursor in software" + System.Environment.NewLine);
            return null;
        }

        public void MoveTo(Point position)
        {
            cursor.Set(position.X, position.Y, true);
        }

        /// <summary>A white filled circle of <paramref name="radius"/> in the top-left of a transparent square image.</summary>
        private static uint[] CreateDot(int radius, int size)
        {
            uint[] pixels = new uint[size * size];

            for (int y = 0; y <= 2 * radius; y++)
            {
                for (int x = 0; x <= 2 * radius; x++)
                {
                    int dx = x - radius;
                    int dy = y - radius;
                    if ((dx * dx) + (dy * dy) <= radius * radius)
                    {
                        pixels[(y * size) + x] = 0xFFFFFFFFu;
                    }
                }
            }

            return pixels;
        }
    }
#pragma warning restore COSMOS0003

    // --- Accumulated state management ---

    private void CreateOrUpdateWindow(RenderCommand cmd)
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

    private void RemoveWindow(int windowId)
    {
        _ = windowCanvases.Remove(windowId);
        _ = windowPositions.Remove(windowId);
        _ = windowPrimitives.Remove(windowId);
        _ = windowOpacities.Remove(windowId);

        if (windowZIndices.TryGetValue(windowId, out int z))
        {
            _ = zOrderedWindows.Remove(ZKey(z, windowId));
            _ = windowZIndices.Remove(windowId);
        }

        isPositionDirty = true;
    }

    private void UpsertPrimitive(RenderCommand cmd)
    {
        if (!windowPrimitives.TryGetValue(cmd.WindowId, out List<(int ElementId, RenderCommand Command)>? list))
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

    private void RemovePrimitives(int windowId, int baseElementId)
    {
        if (!windowPrimitives.TryGetValue(windowId, out List<(int ElementId, RenderCommand Command)>? list))
        {
            return;
        }

        _ = list.RemoveAll(p => p.ElementId >= 0
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
            canvas.DrawCellText(content, color, cmd.Position.X, cmd.Position.Y, fontSize, maxWidth);
        }
    }

    private static void DrawLine(Canvas canvas, RenderCommand cmd)
    {
        Color color = Get(cmd.Properties, "Color", Color.White);
        Point end = Get(cmd.Properties, "EndPosition", cmd.Position);
        canvas.DrawLine(color, cmd.Position.X, cmd.Position.Y, end.X, end.Y);
    }
}
