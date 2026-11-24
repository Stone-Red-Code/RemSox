using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

using RemSox.Shared.Imaging;
using RemSox.Shared.UI.GUI.Rendering;

namespace RemSox.Viewer;

/// <summary>
/// Rebuilds the remote desktop from render commands in a <see cref="Scene"/>, the model the
/// kernel's CanvasRenderSource keeps too: each window is redrawn into its own bitmap when its
/// primitives change. Commands arrive on the network thread; painting happens on the UI thread.
/// </summary>
internal sealed class RemoteScreen : IDisposable
{
    // The kernel lays text out in cells with the proportions of an 8x16 bitmap font.
    private const int FontCellWidth = 8;
    private const int FontCellHeight = 16;

    private readonly Lock stateLock = new();
    private readonly Scene scene = new();
    private readonly Dictionary<int, Bitmap> bitmaps = [];
    private readonly Dictionary<int, Font> fonts = [];

    public Size ScreenSize
    {
        get
        {
            lock (stateLock)
            {
                return scene.ScreenSize;
            }
        }
    }

    public Point CursorPosition
    {
        get
        {
            lock (stateLock)
            {
                return scene.CursorPosition;
            }
        }
    }

    /// <summary>Raised when ScreenInfo changes the remote resolution.</summary>
    public event Action? ScreenSizeChanged;

    public void Apply(RenderCommand command)
    {
        SceneChange change;
        lock (stateLock)
        {
            change = scene.Apply(command);
        }

        if ((change & SceneChange.ScreenSize) != 0)
        {
            ScreenSizeChanged?.Invoke();
        }
    }

    /// <summary>Clears all windows, e.g. before a fresh sync after reconnecting.</summary>
    public void Reset()
    {
        lock (stateLock)
        {
            scene.Clear();
            foreach (Bitmap bitmap in bitmaps.Values)
            {
                bitmap.Dispose();
            }

            bitmaps.Clear();
        }
    }

    /// <summary>Draws the desktop at remote resolution, with (0, 0) at the graphics origin.</summary>
    public void Paint(Graphics g)
    {
        lock (stateLock)
        {
            g.Clear(scene.BackgroundColor);
            if (scene.BackgroundImage is { } image)
            {
                if (backgroundBitmapImage != image)
                {
                    backgroundBitmap?.Dispose();
                    backgroundBitmap = CreateBitmap(image);
                    backgroundBitmapImage = image;
                }

                g.DrawImageUnscaled(backgroundBitmap!, 0, 0);
            }

            foreach (SceneWindow window in scene.Windows)
            {
                Bitmap bitmap = GetBitmap(window);
                if (window.IsDirty)
                {
                    RedrawWindow(window, bitmap);
                }

                DrawWindow(g, window, bitmap);
            }

            foreach (int gone in bitmaps.Keys.Where(id => scene.Find(id) is null).ToList())
            {
                bitmaps[gone].Dispose();
                _ = bitmaps.Remove(gone);
            }

            if (scene.CursorShape is { } shape)
            {
                if (cursorBitmapShape != shape)
                {
                    cursorBitmap?.Dispose();
                    cursorBitmap = CreateBitmap(shape.Image);
                    cursorBitmapShape = shape;
                }

                g.DrawImageUnscaled(cursorBitmap!, shape.GetBounds(scene.CursorPosition).Location);
            }
        }
    }

    // The pointer's image and the background as the kernel sent them, made into a bitmap once each.
    private Bitmap? cursorBitmap;
    private CursorShape? cursorBitmapShape;
    private Bitmap? backgroundBitmap;
    private PixelImage? backgroundBitmapImage;

    // The bitmaps of image primitives, kept while their image is: one replaced by a new image is
    // left to the garbage collector, which releases its GDI+ handle.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PixelImage, Bitmap> imageBitmaps = [];

    private static Bitmap CreateBitmap(PixelImage image)
    {
        Bitmap bitmap = new(image.Width, image.Height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            uint[] pixels = image.Pixels;
            for (int y = 0; y < image.Height; y++)
            {
                int[] row = Array.ConvertAll(pixels[(y * image.Width)..((y + 1) * image.Width)], pixel => (int)pixel);
                System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + (y * data.Stride), image.Width);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    /// <summary>Returns the window's bitmap, made again (and so to be redrawn) when its size changed.</summary>
    private Bitmap GetBitmap(SceneWindow window)
    {
        Size size = new(Math.Max(1, window.Size.Width), Math.Max(1, window.Size.Height));
        if (!bitmaps.TryGetValue(window.Id, out Bitmap? bitmap) || bitmap.Size != size)
        {
            bitmap?.Dispose();
            bitmap = new Bitmap(size.Width, size.Height);
            bitmaps[window.Id] = bitmap;
            RedrawWindow(window, bitmap);
        }

        return bitmap;
    }

    private static void DrawWindow(Graphics g, SceneWindow window, Bitmap bitmap)
    {
        if (window.Opacity == 255)
        {
            g.DrawImageUnscaled(bitmap, window.Position);
            return;
        }

        using ImageAttributes attributes = new();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = window.Opacity / 255f });

        Size size = bitmap.Size;
        g.DrawImage(bitmap, new Rectangle(window.Position, size), 0, 0, size.Width, size.Height, GraphicsUnit.Pixel, attributes);
    }

    public void Dispose()
    {
        Reset();
        cursorBitmap?.Dispose();
        backgroundBitmap?.Dispose();

        foreach (Font font in fonts.Values)
        {
            font.Dispose();
        }
    }

    private void RedrawWindow(SceneWindow window, Bitmap bitmap)
    {
        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(Color.Black);
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        Scene.Draw(window, new GdiPainter(this, g));
        RoundCorners(bitmap, window.CornerRadius);
    }

    /// <summary>
    /// Makes the corners of a window's bitmap as transparent as the kernel leaves them
    /// (<see cref="RoundedCorners"/>), so what is under the window shows there.
    /// </summary>
    private static void RoundCorners(Bitmap bitmap, int radius)
    {
        if (radius <= 0)
        {
            return;
        }

        byte[] cover = RoundedCorners.Fill(radius);
        int right = bitmap.Width - 1;
        int bottom = bitmap.Height - 1;
        for (int y = 0; y < radius; y++)
        {
            for (int x = 0; x < radius; x++)
            {
                byte alpha = cover[(y * radius) + x];
                if (alpha == 255)
                {
                    continue;
                }

                foreach ((int px, int py) in (ReadOnlySpan<(int, int)>)[(x, y), (right - x, y), (x, bottom - y), (right - x, bottom - y)])
                {
                    bitmap.SetPixel(px, py, Color.FromArgb(alpha, bitmap.GetPixel(px, py)));
                }
            }
        }
    }

    /// <summary>Draws primitives with GDI+, which clips by itself.</summary>
    private sealed class GdiPainter(RemoteScreen screen, Graphics g) : IPrimitiveCanvas
    {
        public void FillRectangle(Color color, Rectangle bounds)
        {
            using SolidBrush brush = new(color);
            g.FillRectangle(brush, bounds);
        }

        public void FillRoundedRectangle(Color color, Rectangle bounds, int radius)
        {
            if (radius <= 0)
            {
                FillRectangle(color, bounds);
                return;
            }

            using SolidBrush brush = new(color);
            using GraphicsPath path = RoundedPath(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), radius);
            SmoothingMode before = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillPath(brush, path);
            g.SmoothingMode = before;
        }

        public void DrawRoundedRectangle(Color color, Rectangle bounds, int radius)
        {
            if (radius <= 0)
            {
                DrawRectangle(color, bounds);
                return;
            }

            // A one-pixel pen centred half a pixel inside the edge, like the kernel's outline.
            using Pen pen = new(color);
            using GraphicsPath path = RoundedPath(new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1, bounds.Height - 1), radius - 0.5f);
            SmoothingMode before = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawPath(pen, path);
            g.SmoothingMode = before;
        }

        private static GraphicsPath RoundedPath(RectangleF bounds, float radius)
        {
            float diameter = radius * 2;
            GraphicsPath path = new();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        // Only fills blend a translucent colour; the rest draw it opaque, as the kernel does.
        private static Color Opaque(Color color)
        {
            return color.A == 255 ? color : Color.FromArgb(255, color);
        }

        public void DrawRectangle(Color color, Rectangle bounds)
        {
            using Pen pen = new(Opaque(color));
            g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        }

        public void FillCircle(Color color, Point topLeft, int radius)
        {
            using SolidBrush brush = new(Opaque(color));
            g.FillEllipse(brush, topLeft.X, topLeft.Y, radius * 2, radius * 2);
        }

        public void DrawCircle(Color color, Point topLeft, int radius)
        {
            using Pen pen = new(Opaque(color));
            g.DrawEllipse(pen, topLeft.X, topLeft.Y, radius * 2, radius * 2);
        }

        public void DrawLine(Color color, Point start, Point end)
        {
            using Pen pen = new(Opaque(color));
            g.DrawLine(pen, start, end);
        }

        public void DrawPoint(Color color, Point point)
        {
            using SolidBrush brush = new(Opaque(color));
            g.FillRectangle(brush, point.X, point.Y, 1, 1);
        }

        public void DrawText(string text, Color color, Point position, int fontSize, int maxWidth)
        {
            screen.DrawText(g, text, Opaque(color), position, fontSize, maxWidth);
        }

        public void DrawImage(PixelImage image, Point position)
        {
            // Made once per image: a window is redrawn whenever any of its elements changes.
            if (!imageBitmaps.TryGetValue(image, out Bitmap? bitmap))
            {
                bitmap = CreateBitmap(image);
                imageBitmaps.Add(image, bitmap);
            }

            g.DrawImageUnscaled(bitmap, position);
        }

        public void SetClip(Rectangle? clip)
        {
            if (clip is { } area)
            {
                g.SetClip(area);
            }
            else
            {
                g.ResetClip();
            }
        }
    }

    private void DrawText(Graphics g, string content, Color color, Point position, int height, int maxWidth)
    {
        if (content.Length == 0 || height <= 0)
        {
            return;
        }

        // Same fixed-cell layout as the kernel's DrawCellText, so text lines up with the UI.
        int cellWidth = FontCellWidth * height / FontCellHeight;
        int x = position.X;
        int endX = maxWidth == int.MaxValue ? int.MaxValue : x + maxWidth;

        if (!fonts.TryGetValue(height, out Font? font))
        {
            font = CreateCellFont(height, cellWidth);
            fonts[height] = font;
        }

        using SolidBrush brush = new(color);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        foreach (char c in content)
        {
            if (x + cellWidth > endX)
            {
                break;
            }

            if (!char.IsWhiteSpace(c))
            {
                g.DrawString(c.ToString(), font, brush, new RectangleF(x - cellWidth, position.Y, cellWidth * 3, height), cellFormat);
            }

            x += cellWidth;
        }

        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
    }

    // DejaVu Sans Mono's advance and line height, as fractions of its em size.
    private const float FontAdvanceEm = 0.602f;
    private const float FontLineHeightEm = 1.164f;

    private static readonly StringFormat cellFormat = new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
    };

    private static readonly Lazy<FontFamily> uiFontFamily = new(LoadUiFontFamily);
    private static PrivateFontCollection? uiFontCollection;

    /// <summary>
    /// The UI font at the largest em size whose glyphs fit a <paramref name="cellWidth"/> by
    /// <paramref name="height"/> cell, as the kernel sizes it.
    /// </summary>
    private static Font CreateCellFont(int height, int cellWidth)
    {
        float emPx = Math.Max(1f, Math.Min(height / FontLineHeightEm, cellWidth / FontAdvanceEm));
        return new Font(uiFontFamily.Value, emPx, GraphicsUnit.Pixel);
    }

    /// <summary>Loads the embedded DejaVu Sans Mono, the kernel's UI font, or falls back to the system monospace font.</summary>
    private static FontFamily LoadUiFontFamily()
    {
        using Stream? stream = typeof(RemoteScreen).Assembly.GetManifestResourceStream("DejaVuSansMono.ttf");
        if (stream is null)
        {
            return FontFamily.GenericMonospace;
        }

        byte[] data = new byte[stream.Length];
        stream.ReadExactly(data);

        // AddMemoryFont keeps using the memory, so it stays allocated for the life of the process.
        IntPtr memory = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(data.Length);
        System.Runtime.InteropServices.Marshal.Copy(data, 0, memory, data.Length);

        uiFontCollection = new PrivateFontCollection();
        uiFontCollection.AddMemoryFont(memory, data.Length);
        return uiFontCollection.Families[0];
    }
}
