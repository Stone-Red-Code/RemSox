using RemSox.Shared.Imaging;
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
    private int[] underCursor = [];
    private Rectangle underCursorArea = Rectangle.Empty;

    // The display's own cursor, when its driver has one (VMware SVGA, Intel, AMD): the display
    // composes it, so a cursor move costs neither drawing nor a Display() copy. It is used while
    // it accepts the scene's cursor shape; one it refuses is drawn in software.
    private HardwareCursor? hardwareCursor;
    private bool hardwareCursorProbed;
    private bool hardwareCursorShows;
    private bool cursorShapeChanged = true;

    public void Render(IEnumerable<RenderCommand> commands)
    {
        foreach (RenderCommand command in commands)
        {
            SceneChange change = scene.Apply(command);
            if ((change & SceneChange.Layout) != 0)
            {
                isPositionDirty = true;
            }

            if ((change & SceneChange.CursorShape) != 0)
            {
                cursorShapeChanged = true;
            }

            if ((change & SceneChange.Background) != 0)
            {
                backgroundPixels = ToOpaquePixels(scene.BackgroundImage);
            }
        }
    }

    // The scene's background image as the canvas copies it: opaque pixels in an int array.
    private int[]? backgroundPixels;

    private static int[]? ToOpaquePixels(PixelImage? image)
    {
        if (image is null)
        {
            return null;
        }

        int[] pixels = new int[image.Pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (int)(image.Pixels[i] | 0xFF000000u);
        }

        return pixels;
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

        bool shapeChanged = cursorShapeChanged;
        if (shapeChanged)
        {
            ChangeCursorShape();
        }

        // A new shape is drawn like a move: the old one's pixels put back, the new one drawn.
        bool pointerMoved = pointerPosition != lastPointerPosition || shapeChanged;
        bool sceneDirty = isPositionDirty || scene.AnyDirty;

        if (!sceneDirty && !pointerMoved)
        {
            return;
        }

        if (hardwareCursorShows)
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
                hardwareCursor!.MoveTo(pointerPosition);
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

        if (scene.CursorShape is { } shape)
        {
            if (screen is not null)
            {
                SaveUnderCursor(screenCanvas, screen, shape, pointerPosition);
            }

            DrawCursor(screenCanvas, screen, shape, pointerPosition);
        }

        screenCanvas.Display();

        lastPointerPosition = pointerPosition;
    }

    /// <summary>
    /// Takes the scene's new cursor shape: shows it with the display's hardware cursor when that
    /// accepts it, else draws it in software from now on. The scene is composited again only when
    /// the hardware cursor takes over from a software one, to remove what software drew.
    /// </summary>
    private void ChangeCursorShape()
    {
        cursorShapeChanged = false;

        bool probing = !hardwareCursorProbed;
        if (probing)
        {
            hardwareCursorProbed = true;
            hardwareCursor = HardwareCursor.TryCreate();
            isPositionDirty = true;
        }

        bool showed = hardwareCursorShows;
        hardwareCursorShows = hardwareCursor is not null && scene.CursorShape is { } shape && hardwareCursor.TryShow(shape);
        if (hardwareCursorShows)
        {
            hardwareCursor!.MoveTo(scene.CursorPosition);
            if (!showed)
            {
                isPositionDirty = true;
                underCursorArea = Rectangle.Empty;
                Cosmos.Kernel.System.Diagnostics.Log.WriteString($"[RemSox] Showing the pointer with the hardware cursor of {hardwareCursor.DisplayName}" + System.Environment.NewLine);
            }
        }
        else if (hardwareCursor is not null && (showed || probing))
        {
            if (showed)
            {
                hardwareCursor.Hide();
            }

            Cosmos.Kernel.System.Diagnostics.Log.WriteString($"[RemSox] {hardwareCursor.DisplayName} refused the pointer's image; drawing it in software" + System.Environment.NewLine);
        }
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

    /// <summary>
    /// Draws the background, the image when it is the screen's size (the screen scaled it so)
    /// else the colour, and every window over it, in Z order.
    /// </summary>
    private void CompositeScene(Canvas screenCanvas)
    {
        int w = (int)screenCanvas.Mode.Width;
        int h = (int)screenCanvas.Mode.Height;
        if (backgroundPixels is not null && scene.BackgroundImage is { } image && image.Width == w && image.Height == h)
        {
            screenCanvas.DrawArray(backgroundPixels, 0, 0, w, h);
        }
        else
        {
            screenCanvas.Clear(scene.BackgroundColor);
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

    /// <summary>The part of <paramref name="shape"/>'s image, with the pointer at <paramref name="position"/>, that is on the screen.</summary>
    private static Rectangle GetCursorArea(Canvas screenCanvas, CursorShape shape, Point position)
    {
        Rectangle area = shape.GetBounds(position);
        area.Intersect(new Rectangle(0, 0, screenCanvas.Width, screenCanvas.Height));
        return area;
    }

    private void SaveUnderCursor(Canvas screenCanvas, int[] screen, CursorShape shape, Point position)
    {
        underCursorArea = GetCursorArea(screenCanvas, shape, position);
        if (underCursor.Length < underCursorArea.Width * underCursorArea.Height)
        {
            underCursor = new int[shape.Width * shape.Height];
        }

        for (int row = 0; row < underCursorArea.Height; row++)
        {
            Array.Copy(screen, ((underCursorArea.Y + row) * screenCanvas.Width) + underCursorArea.X, underCursor, row * underCursorArea.Width, underCursorArea.Width);
        }
    }

    private void RestoreUnderCursor(Canvas screenCanvas, int[] screen)
    {
        for (int row = 0; row < underCursorArea.Height; row++)
        {
            Array.Copy(underCursor, row * underCursorArea.Width, screen, ((underCursorArea.Y + row) * screenCanvas.Width) + underCursorArea.X, underCursorArea.Width);
        }
    }

    /// <summary>
    /// Draws <paramref name="shape"/> with the pointer at <paramref name="position"/>, blending
    /// its translucent pixels (edges, shadow) over the scene: straight into the screen's buffer
    /// when there is one, else point by point.
    /// </summary>
    private static void DrawCursor(Canvas screenCanvas, int[]? screen, CursorShape shape, Point position)
    {
        Rectangle bounds = shape.GetBounds(position);
        Rectangle area = GetCursorArea(screenCanvas, shape, position);
        uint[] pixels = shape.Image.Pixels;

        for (int y = area.Top; y < area.Bottom; y++)
        {
            int source = ((y - bounds.Y) * shape.Width) + (area.X - bounds.X);
            for (int x = area.Left; x < area.Right; x++, source++)
            {
                uint pixel = pixels[source];
                uint alpha = pixel >> 24;
                if (alpha == 0)
                {
                    continue;
                }

                if (screen is not null)
                {
                    int at = (y * screenCanvas.Width) + x;
                    screen[at] = (int)Blend(pixel, (uint)screen[at], alpha);
                }
                else if (alpha >= 128)
                {
                    screenCanvas.DrawPoint(Color.FromArgb((int)(pixel | 0xFF000000u)), x, y);
                }
            }
        }
    }

    /// <summary>Lays <paramref name="source"/> over the opaque <paramref name="target"/> at <paramref name="alpha"/> (0-255).</summary>
    private static uint Blend(uint source, uint target, uint alpha)
    {
        if (alpha == 255)
        {
            return source;
        }

        uint weight = alpha + 1;          // 1..256
        uint inverse = 256 - weight;
        uint redBlue = ((((source & 0xFF00FFu) * weight) + ((target & 0xFF00FFu) * inverse)) >> 8) & 0xFF00FFu;
        uint green = ((((source & 0x00FF00u) * weight) + ((target & 0x00FF00u) * inverse)) >> 8) & 0x00FF00u;
        return 0xFF000000u | redBlue | green;
    }

    /// <summary>The display's hardware cursor, showing the same shape the software cursor draws.</summary>
#pragma warning disable COSMOS0003 // IHardwareCursor is an experimental driver kit seam.
    private sealed class HardwareCursor(DisplayDevice display, IHardwareCursor cursor)
    {
        // Square sizes tried after the shape's own: the fixed sizes cursor planes take.
        private static readonly int[] PlaneSizes = [32, 64];

        /// <summary>Returns the primary display's hardware cursor, or null when it has none.</summary>
        public static HardwareCursor? TryCreate()
        {
            DisplayDevice? display = DisplayManager.IsEnabled ? DisplayManager.Primary : null;
            return display is not null && display.TryGetFacet(out IHardwareCursor? cursor) && cursor is not null
                ? new HardwareCursor(display, cursor)
                : null;
        }

        /// <summary>Gets the display's name and driver, for the serial log.</summary>
        public string DisplayName => $"{display.Name} ({display.DriverName})";

        /// <summary>
        /// Makes the cursor show <paramref name="shape"/>, at its own size or padded into a size
        /// a cursor plane takes; false when the display refuses all of them.
        /// </summary>
        public bool TryShow(CursorShape shape)
        {
            if (cursor.TryDefine(shape.Hotspot.X, shape.Hotspot.Y, shape.Width, shape.Height, shape.Image.Pixels))
            {
                return true;
            }

            foreach (int size in PlaneSizes)
            {
                if (size >= shape.Width && size >= shape.Height
                    && cursor.TryDefine(shape.Hotspot.X, shape.Hotspot.Y, size, size, Pad(shape, size)))
                {
                    return true;
                }
            }

            return false;
        }

        public void MoveTo(Point position)
        {
            cursor.Set(position.X, position.Y, true);
        }

        public void Hide()
        {
            cursor.Set(0, 0, false);
        }

        /// <summary>The shape's image in the top-left of a transparent square of <paramref name="size"/>.</summary>
        private static uint[] Pad(CursorShape shape, int size)
        {
            uint[] pixels = new uint[size * size];

            for (int y = 0; y < shape.Height; y++)
            {
                Array.Copy(shape.Image.Pixels, y * shape.Width, pixels, y * size, shape.Width);
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
