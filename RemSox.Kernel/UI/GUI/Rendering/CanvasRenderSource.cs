using RemSox.Shared.UI.GUI.Rendering;
using Cosmos.Kernel.HAL.Devices.Display;
using Cosmos.Kernel.System.Graphics;

using RemSox.Kernel.Utils;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.Rendering;

/// <summary>
/// Draws one screen on the machine's display: applies render commands to a <see cref="Scene"/>
/// (the model the remote desktop viewer keeps too), draws each window that changed into a canvas
/// of its own, and composites them with the pointer.
/// </summary>
public sealed class CanvasRenderSource : IRenderSource
{
    private readonly Scene scene = new();
    private readonly Dictionary<int, Canvas> windowCanvases = [];
    private readonly CanvasPainter painter = new();

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

    public void Render(IEnumerable<RenderCommand> commands)
    {
        foreach (RenderCommand command in commands)
        {
            if ((scene.Apply(command) & SceneChange.Layout) != 0)
            {
                isPositionDirty = true;
            }
        }
    }

    /// <summary>
    /// Forgets every window, for showing another screen: that screen's windows then draw
    /// themselves again (<see cref="Windows.Screen.InvalidateAll"/>).
    /// </summary>
    public void Clear()
    {
        scene.Clear();
        windowCanvases.Clear();
        isPositionDirty = true;
    }

    public void Composite()
    {
        Canvas screenCanvas = Canvas.GetFullScreen();
        Point pointerPosition = scene.CursorPosition;

        bool pointerMoved = pointerPosition != lastPointerPosition;
        bool sceneDirty = isPositionDirty || scene.AnyDirty;

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

    /// <summary>
    /// Draws the windows whose primitives changed into their canvases (made, or made again at a
    /// new size, here), and drops the canvases of windows that are gone.
    /// </summary>
    private void RedrawDirtyWindows()
    {
        foreach (SceneWindow window in scene.Windows)
        {
            if (!windowCanvases.TryGetValue(window.Id, out Canvas? canvas)
                || canvas.Width != window.Size.Width || canvas.Height != window.Size.Height)
            {
                canvas = new Canvas(Math.Max(1, window.Size.Width), Math.Max(1, window.Size.Height));
                windowCanvases[window.Id] = canvas;
                window.Invalidate();
            }

            if (window.IsDirty)
            {
                canvas.Clear(Color.Black);
                painter.Begin(canvas);
                Scene.Draw(window, painter);
            }
        }

        if (windowCanvases.Count > scene.Windows.Count)
        {
            foreach (int id in windowCanvases.Keys.Where(id => scene.Find(id) is null).ToList())
            {
                _ = windowCanvases.Remove(id);
            }
        }
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

        foreach (SceneWindow window in scene.Windows)
        {
            CompositeWindow(screenCanvas, window);
        }
    }

    private void CompositeWindow(Canvas screenCanvas, SceneWindow window)
    {
        if (!windowCanvases.TryGetValue(window.Id, out Canvas? wc))
        {
            return;
        }

        Point pos = window.Position;
        byte opacity = window.Opacity;
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

    /// <summary>
    /// Draws primitives on a window's canvas. A clip rectangle is honoured by drawing into a
    /// scratch canvas the size of the rectangle, holding the window's pixels there, and copying
    /// it back when the clip ends: Cosmos's canvas clips only at its own edges.
    /// </summary>
    private sealed class CanvasPainter : IPrimitiveCanvas
    {
        private Canvas window = null!;
        private Canvas target = null!;
        private Point offset;
        private Rectangle clipArea;
        private bool hidden;
        private Canvas? scratch;

        public void Begin(Canvas canvas)
        {
            window = canvas;
            target = canvas;
            offset = Point.Empty;
            hidden = false;
        }

        public void SetClip(Rectangle? clip)
        {
            EndClip();
            if (clip is not { } requested)
            {
                return;
            }

            Rectangle area = Rectangle.Intersect(requested, new Rectangle(0, 0, window.Width, window.Height));
            if (area.IsEmpty)
            {
                hidden = true;
                return;
            }

            int[]? source = window.GetBuffer();
            if (scratch is null || scratch.Width != area.Width || scratch.Height != area.Height)
            {
                scratch = new Canvas(area.Width, area.Height);
            }

            int[]? copy = scratch.GetBuffer();
            if (source is null || copy is null)
            {
                return; // No pixels to work on: draw unclipped rather than not at all.
            }

            for (int row = 0; row < area.Height; row++)
            {
                Array.Copy(source, ((area.Y + row) * window.Width) + area.X, copy, row * area.Width, area.Width);
            }

            clipArea = area;
            target = scratch;
            offset = new Point(-area.X, -area.Y);
        }

        private void EndClip()
        {
            hidden = false;
            if (target == window)
            {
                return;
            }

            int[] source = target.GetBuffer()!;
            int[] pixels = window.GetBuffer()!;
            for (int row = 0; row < clipArea.Height; row++)
            {
                Array.Copy(source, row * clipArea.Width, pixels, ((clipArea.Y + row) * window.Width) + clipArea.X, clipArea.Width);
            }

            target = window;
            offset = Point.Empty;
        }

        public void FillRectangle(Color color, Rectangle bounds)
        {
            if (!hidden)
            {
                target.DrawFilledRectangle(color, bounds.X + offset.X, bounds.Y + offset.Y, bounds.Width, bounds.Height);
            }
        }

        public void DrawRectangle(Color color, Rectangle bounds)
        {
            if (!hidden)
            {
                target.DrawRectangle(color, bounds.X + offset.X, bounds.Y + offset.Y, bounds.Width, bounds.Height);
            }
        }

        public void FillCircle(Color color, Point topLeft, int radius)
        {
            if (!hidden)
            {
                target.DrawFilledCircle(color, topLeft.X + offset.X + radius, topLeft.Y + offset.Y + radius, radius);
            }
        }

        public void DrawCircle(Color color, Point topLeft, int radius)
        {
            if (!hidden)
            {
                target.DrawCircle(color, topLeft.X + offset.X + radius, topLeft.Y + offset.Y + radius, radius);
            }
        }

        public void DrawLine(Color color, Point start, Point end)
        {
            if (!hidden)
            {
                target.DrawLine(color, start.X + offset.X, start.Y + offset.Y, end.X + offset.X, end.Y + offset.Y);
            }
        }

        public void DrawPoint(Color color, Point point)
        {
            if (!hidden)
            {
                target.DrawPoint(color, point.X + offset.X, point.Y + offset.Y);
            }
        }

        public void DrawText(string text, Color color, Point position, int fontSize, int maxWidth)
        {
            if (!hidden)
            {
                target.DrawCellText(text, color, position.X + offset.X, position.Y + offset.Y, fontSize, maxWidth);
            }
        }
    }
}
