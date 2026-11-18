using System.Drawing.Drawing2D;
using System.Drawing.Text;

using RemSox.Shared.UI.GUI.Rendering;

namespace RemSox.Viewer;

/// <summary>
/// Rebuilds the remote desktop from render commands, mirroring the kernel's CanvasRenderSource:
/// each window keeps its primitives in draw order and is redrawn into its own bitmap when they change.
/// Commands arrive on the network thread; painting happens on the UI thread.
/// </summary>
internal sealed class RemoteScreen : IDisposable
{
    // Matches UIElement.PrimitiveIdShift in the kernel.
    private const int PrimitiveIdShift = 6;

    // The kernel draws text with an 8x16 bitmap font scaled to the requested height.
    private const int FontCellWidth = 8;
    private const int FontCellHeight = 16;

    private static readonly Color[] backgroundBands =
    [
        Color.FromArgb(30, 30, 35),
        Color.FromArgb(45, 45, 48),
        Color.FromArgb(60, 60, 65),
        Color.FromArgb(45, 45, 48),
    ];

    private readonly Lock stateLock = new();
    private readonly Dictionary<int, RemoteWindow> windows = [];
    private readonly Dictionary<int, Font> fonts = [];

    public Size ScreenSize { get; private set; } = new(640, 480);

    public Point CursorPosition { get; private set; }

    /// <summary>Raised when ScreenInfo changes the remote resolution.</summary>
    public event Action? ScreenSizeChanged;

    public void Apply(RenderCommand command)
    {
        bool sizeChanged = false;

        lock (stateLock)
        {
            switch (command.Type)
            {
                case RenderCommandType.ScreenInfo:
                    Size size = new(Get(command, "Width", 640), Get(command, "Height", 480));
                    sizeChanged = size != ScreenSize;
                    ScreenSize = size;
                    break;

                case RenderCommandType.SetCursor:
                    CursorPosition = command.Position;
                    break;

                case RenderCommandType.CreateWindow:
                    CreateOrUpdateWindow(command);
                    break;

                case RenderCommandType.DestroyWindow:
                    if (windows.Remove(command.WindowId, out RemoteWindow? removed))
                    {
                        removed.Dispose();
                    }
                    break;

                case RenderCommandType.MoveWindow:
                    if (windows.TryGetValue(command.WindowId, out RemoteWindow? moved))
                    {
                        moved.Position = command.Position;
                    }
                    break;

                case RenderCommandType.RemovePrimitives:
                    if (windows.TryGetValue(command.WindowId, out RemoteWindow? target))
                    {
                        _ = target.Primitives.RemoveAll(p => p.ElementId >= 0
                            ? (p.ElementId >> PrimitiveIdShift) == command.ElementId
                            : p.ElementId == command.ElementId);
                        target.Dirty = true;
                    }
                    break;

                default:
                    if (windows.TryGetValue(command.WindowId, out RemoteWindow? window))
                    {
                        int index = window.Primitives.FindIndex(p => p.ElementId == command.ElementId);
                        if (index >= 0)
                        {
                            window.Primitives[index] = (command.ElementId, command);
                        }
                        else
                        {
                            window.Primitives.Add((command.ElementId, command));
                        }

                        window.Dirty = true;
                    }
                    break;
            }
        }

        if (sizeChanged)
        {
            ScreenSizeChanged?.Invoke();
        }
    }

    /// <summary>Clears all windows, e.g. before a fresh sync after reconnecting.</summary>
    public void Reset()
    {
        lock (stateLock)
        {
            foreach (RemoteWindow window in windows.Values)
            {
                window.Dispose();
            }

            windows.Clear();
        }
    }

    /// <summary>Draws the desktop at remote resolution, with (0, 0) at the graphics origin.</summary>
    public void Paint(Graphics g)
    {
        lock (stateLock)
        {
            int width = ScreenSize.Width;
            int height = ScreenSize.Height;
            int bandHeight = height / backgroundBands.Length;

            for (int i = 0; i < backgroundBands.Length; i++)
            {
                using SolidBrush brush = new(backgroundBands[i]);
                g.FillRectangle(brush, 0, i * bandHeight, width, bandHeight + 1);
            }

            foreach (RemoteWindow window in windows.Values.OrderBy(w => w.ZIndex).ThenBy(w => w.Id))
            {
                if (window.Dirty)
                {
                    RedrawWindow(window);
                }

                g.DrawImageUnscaled(window.Bitmap, window.Position);
            }

            g.FillEllipse(Brushes.White, CursorPosition.X - 5, CursorPosition.Y - 5, 10, 10);
        }
    }

    public void Dispose()
    {
        Reset();

        foreach (Font font in fonts.Values)
        {
            font.Dispose();
        }
    }

    private void CreateOrUpdateWindow(RenderCommand command)
    {
        Size size = Get(command, "Size", new Size(160, 120));

        if (!windows.TryGetValue(command.WindowId, out RemoteWindow? window))
        {
            window = new RemoteWindow(command.WindowId);
            windows[command.WindowId] = window;
        }

        window.Resize(size);
        window.Position = command.Position;
        window.ZIndex = Get(command, "ZIndex", 0);
        window.Dirty = true;
    }

    private void RedrawWindow(RemoteWindow window)
    {
        using Graphics g = Graphics.FromImage(window.Bitmap);
        g.Clear(Color.Black);
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        foreach ((int _, RenderCommand command) in window.Primitives)
        {
            DrawPrimitive(g, command);
        }

        window.Dirty = false;
    }

    private void DrawPrimitive(Graphics g, RenderCommand command)
    {
        Color color = Get(command, "Color", Color.White);
        Point pos = command.Position;

        switch (command.Type)
        {
            case RenderCommandType.DrawFilledRect:
            {
                Size size = Get(command, "Size", new Size(10, 10));
                using SolidBrush brush = new(color);
                g.FillRectangle(brush, pos.X, pos.Y, size.Width, size.Height);
                break;
            }

            case RenderCommandType.DrawRectBorder:
            {
                Size size = Get(command, "Size", new Size(10, 10));
                using Pen pen = new(color);
                g.DrawRectangle(pen, pos.X, pos.Y, size.Width - 1, size.Height - 1);
                break;
            }

            case RenderCommandType.DrawFilledCircle:
            {
                int radius = Get(command, "Radius", 10);
                using SolidBrush brush = new(color);
                g.FillEllipse(brush, pos.X, pos.Y, radius * 2, radius * 2);
                break;
            }

            case RenderCommandType.DrawCircle:
            {
                int radius = Get(command, "Radius", 10);
                using Pen pen = new(color);
                g.DrawEllipse(pen, pos.X, pos.Y, radius * 2, radius * 2);
                break;
            }

            case RenderCommandType.DrawPoint:
            {
                using SolidBrush brush = new(color);
                g.FillRectangle(brush, pos.X, pos.Y, 1, 1);
                break;
            }

            case RenderCommandType.DrawLine:
            {
                Point end = Get(command, "EndPosition", pos);
                using Pen pen = new(color);
                g.DrawLine(pen, pos, end);
                break;
            }

            case RenderCommandType.DrawText:
                DrawText(g, command, color);
                break;
        }
    }

    private void DrawText(Graphics g, RenderCommand command, Color color)
    {
        string content = Get(command, "Content", string.Empty);
        // Deserialized as a byte (it is a single byte on the wire).
        int height = command.Properties.TryGetValue("FontSize", out object? rawSize) && rawSize is not null ? Convert.ToInt32(rawSize) : 12;
        int maxWidth = Get(command, "MaxWidth", int.MaxValue);

        if (content.Length == 0 || height <= 0)
        {
            return;
        }

        // Same fixed-cell layout as the kernel's DrawStringHeight, so text lines up with the UI.
        int cellWidth = FontCellWidth * height / FontCellHeight;
        int x = command.Position.X;
        int endX = maxWidth == int.MaxValue ? int.MaxValue : x + maxWidth;

        if (!fonts.TryGetValue(height, out Font? font))
        {
            font = new Font(FontFamily.GenericMonospace, height * 0.75f, GraphicsUnit.Pixel);
            fonts[height] = font;
        }

        using SolidBrush brush = new(color);

        foreach (char c in content)
        {
            if (x + cellWidth > endX)
            {
                break;
            }

            if (!char.IsWhiteSpace(c))
            {
                TextRenderer.DrawText(g, c.ToString(), font, new Rectangle(x - (cellWidth / 2), command.Position.Y, cellWidth * 2, height),
                    color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }

            x += cellWidth;
        }
    }

    private static T Get<T>(RenderCommand command, string key, T fallback)
    {
        return command.Properties.TryGetValue(key, out object? raw) && raw is T value ? value : fallback;
    }

    private sealed class RemoteWindow(int id) : IDisposable
    {
        public int Id { get; } = id;

        public Point Position { get; set; }

        public int ZIndex { get; set; }

        public bool Dirty { get; set; } = true;

        public Bitmap Bitmap { get; private set; } = new(1, 1);

        public List<(int ElementId, RenderCommand Command)> Primitives { get; } = [];

        public void Resize(Size size)
        {
            if (Bitmap.Size != size && size.Width > 0 && size.Height > 0)
            {
                Bitmap.Dispose();
                Bitmap = new Bitmap(size.Width, size.Height);
            }
        }

        public void Dispose()
        {
            Bitmap.Dispose();
        }
    }
}
