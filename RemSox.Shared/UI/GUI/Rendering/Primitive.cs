using System.Drawing;

using RemSox.Shared.Imaging;

namespace RemSox.Shared.UI.GUI.Rendering;

/// <summary>
/// What a renderer draws primitives on: the kernel's canvas or the viewer's bitmap. Coordinates
/// are the window's own pixels; nothing is drawn outside <see cref="SetClip"/>'s rectangle while
/// one is set, or outside the window.
/// </summary>
public interface IPrimitiveCanvas
{
    void FillRectangle(Color color, Rectangle bounds);

    /// <summary>Draws a one-pixel outline just inside <paramref name="bounds"/>.</summary>
    void DrawRectangle(Color color, Rectangle bounds);

    /// <summary>Fills the circle whose bounding square starts at <paramref name="topLeft"/>.</summary>
    void FillCircle(Color color, Point topLeft, int radius);

    /// <inheritdoc cref="FillCircle"/>
    void DrawCircle(Color color, Point topLeft, int radius);

    void DrawLine(Color color, Point start, Point end);

    void DrawPoint(Color color, Point point);

    /// <summary>
    /// Draws <paramref name="text"/> one character per fixed cell <paramref name="fontSize"/>
    /// pixels high; characters that would end past <paramref name="maxWidth"/> are left out.
    /// </summary>
    void DrawText(string text, Color color, Point position, int fontSize, int maxWidth);

    /// <summary>
    /// Draws <paramref name="image"/> at its own size with its top left at
    /// <paramref name="position"/>. Its pixels are drawn as opaque: whoever made it flattened any
    /// transparency first (<see cref="ImagePrimitive"/>).
    /// </summary>
    void DrawImage(PixelImage image, Point position);

    /// <summary>Limits what is drawn from now on to <paramref name="clip"/>, or lifts the limit for null.</summary>
    void SetClip(Rectangle? clip);
}

/// <summary>
/// A drawing primitive of one window: a rectangle, circle, line, point or text. A window keeps
/// its primitives in a <see cref="PrimitiveList"/>, which orders them by
/// <see cref="ZIndex"/> and id, and draws them on an <see cref="IPrimitiveCanvas"/>.
/// </summary>
/// <remarks>
/// After the common header a primitive carries its z-index, a flags byte (bit 0: a clip
/// rectangle follows, x, y as int16 and width, height as uint16), its position and color, then
/// what its kind needs.
/// </remarks>
public abstract class Primitive : RenderCommand
{
    private const byte HasClip = 1;

    public Point Position { get; init; }

    public Color Color { get; init; } = Color.White;

    /// <summary>
    /// Gets or sets where the primitive's element goes among the window's others, from the
    /// element's z-index (see <see cref="PrimitiveList"/>). Set by the window as it sends the
    /// element, and sent with every primitive, so a change moves the element at the next update.
    /// </summary>
    public int ZIndex { get; set; }

    /// <summary>
    /// Gets or sets the rectangle, in window pixels, outside which nothing of the primitive is
    /// drawn, or null for none: set for the elements of a panel that scrolls, so what is
    /// scrolled out of it is not seen.
    /// </summary>
    public Rectangle? Clip { get; set; }

    /// <summary>Draws the primitive (its <see cref="Clip"/> is the caller's: see <see cref="Scene.Draw"/>).</summary>
    public abstract void Draw(IPrimitiveCanvas canvas);

    protected sealed override void WriteBody(Stream s)
    {
        Wire.WriteVarint(s, ZIndex);
        s.WriteByte(Clip is null ? (byte)0 : HasClip);
        if (Clip is { } clip)
        {
            Wire.WritePoint(s, clip.Location);
            Wire.WriteUInt16(s, clip.Width);
            Wire.WriteUInt16(s, clip.Height);
        }

        Wire.WritePoint(s, Position);
        Wire.WriteColor(s, Color);
        WriteShape(s);
    }

    /// <summary>Writes what this kind of primitive carries after its position and color.</summary>
    protected abstract void WriteShape(Stream s);

    internal static Primitive Read(RenderCommandType type, int windowId, int elementId, Wire.Reader r)
    {
        int zIndex = r.Varint();
        byte flags = r.Byte();
        Rectangle? clip = (flags & HasClip) != 0 ? new Rectangle(r.Point(), new Size(r.UInt16(), r.UInt16())) : null;
        Point position = r.Point();
        Color color = r.Color();

        Primitive primitive = type switch
        {
            RenderCommandType.DrawFilledRect => new FilledRectangle { WindowId = windowId, ElementId = elementId, Position = position, Color = color, Size = new Size(r.UInt16(), r.UInt16()) },
            RenderCommandType.DrawRectBorder => new RectangleBorder { WindowId = windowId, ElementId = elementId, Position = position, Color = color, Size = new Size(r.UInt16(), r.UInt16()) },
            RenderCommandType.DrawFilledCircle => new FilledCircle { WindowId = windowId, ElementId = elementId, Position = position, Color = color, Radius = r.UInt16() },
            RenderCommandType.DrawCircle => new CircleOutline { WindowId = windowId, ElementId = elementId, Position = position, Color = color, Radius = r.UInt16() },
            RenderCommandType.DrawText => new TextPrimitive { WindowId = windowId, ElementId = elementId, Position = position, Color = color, FontSize = r.Byte(), Content = r.String(), MaxWidth = r.Varint() },
            RenderCommandType.DrawLine => new LinePrimitive { WindowId = windowId, ElementId = elementId, Position = position, Color = color, End = r.Point() },
            RenderCommandType.DrawPoint => new PointPrimitive { WindowId = windowId, ElementId = elementId, Position = position, Color = color },
            RenderCommandType.DrawImage => new ImagePrimitive { WindowId = windowId, ElementId = elementId, Position = position, Color = color, Image = Qoi.Read(r.Bytes()) },
            _ => throw new InvalidDataException($"Unknown primitive {(int)type}."),
        };

        primitive.ZIndex = zIndex;
        primitive.Clip = clip;
        return primitive;
    }
}

public sealed class FilledRectangle : Primitive
{
    public override RenderCommandType Type => RenderCommandType.DrawFilledRect;

    public Size Size { get; init; }

    public override void Draw(IPrimitiveCanvas canvas)
    {
        canvas.FillRectangle(Color, new Rectangle(Position, Size));
    }

    protected override void WriteShape(Stream s)
    {
        Wire.WriteUInt16(s, Size.Width);
        Wire.WriteUInt16(s, Size.Height);
    }
}

public sealed class RectangleBorder : Primitive
{
    public override RenderCommandType Type => RenderCommandType.DrawRectBorder;

    public Size Size { get; init; }

    public override void Draw(IPrimitiveCanvas canvas)
    {
        canvas.DrawRectangle(Color, new Rectangle(Position, Size));
    }

    protected override void WriteShape(Stream s)
    {
        Wire.WriteUInt16(s, Size.Width);
        Wire.WriteUInt16(s, Size.Height);
    }
}

/// <summary>A filled circle; <see cref="Primitive.Position"/> is the top left of its bounding square.</summary>
public sealed class FilledCircle : Primitive
{
    public override RenderCommandType Type => RenderCommandType.DrawFilledCircle;

    public int Radius { get; init; }

    public override void Draw(IPrimitiveCanvas canvas)
    {
        canvas.FillCircle(Color, Position, Radius);
    }

    protected override void WriteShape(Stream s)
    {
        Wire.WriteUInt16(s, Radius);
    }
}

/// <summary>A circle's outline; <see cref="Primitive.Position"/> is the top left of its bounding square.</summary>
public sealed class CircleOutline : Primitive
{
    public override RenderCommandType Type => RenderCommandType.DrawCircle;

    public int Radius { get; init; }

    public override void Draw(IPrimitiveCanvas canvas)
    {
        canvas.DrawCircle(Color, Position, Radius);
    }

    protected override void WriteShape(Stream s)
    {
        Wire.WriteUInt16(s, Radius);
    }
}

/// <summary>A line of text in fixed cells (see <see cref="IPrimitiveCanvas.DrawText"/>).</summary>
public sealed class TextPrimitive : Primitive
{
    public override RenderCommandType Type => RenderCommandType.DrawText;

    public string Content { get; init; } = string.Empty;

    /// <summary>Gets the height of a character cell in pixels, 1-255.</summary>
    public int FontSize { get; init; } = 12;

    public int MaxWidth { get; init; } = int.MaxValue;

    public override void Draw(IPrimitiveCanvas canvas)
    {
        if (Content.Length > 0 && FontSize > 0)
        {
            canvas.DrawText(Content, Color, Position, FontSize, MaxWidth);
        }
    }

    protected override void WriteShape(Stream s)
    {
        s.WriteByte((byte)Math.Clamp(FontSize, 0, 255));
        Wire.WriteString(s, Content);
        Wire.WriteVarint(s, MaxWidth);
    }
}

public sealed class LinePrimitive : Primitive
{
    public override RenderCommandType Type => RenderCommandType.DrawLine;

    public Point End { get; init; }

    public override void Draw(IPrimitiveCanvas canvas)
    {
        canvas.DrawLine(Color, Position, End);
    }

    protected override void WriteShape(Stream s)
    {
        Wire.WritePoint(s, End);
    }
}

public sealed class PointPrimitive : Primitive
{
    public override RenderCommandType Type => RenderCommandType.DrawPoint;

    public override void Draw(IPrimitiveCanvas canvas)
    {
        canvas.DrawPoint(Color, Position);
    }

    protected override void WriteShape(Stream s)
    {
    }
}

/// <summary>
/// An image drawn at its own size, its top left at <see cref="Primitive.Position"/>: an image
/// viewer's picture, already scaled by the kernel to the size it shows. It goes to viewers as
/// QOI (<see cref="Qoi"/>). Its pixels are opaque; the kernel flattens transparency before.
/// </summary>
public sealed class ImagePrimitive : Primitive
{
    public override RenderCommandType Type => RenderCommandType.DrawImage;

    public required PixelImage Image { get; init; }

    public override void Draw(IPrimitiveCanvas canvas)
    {
        canvas.DrawImage(Image, Position);
    }

    protected override void WriteShape(Stream s)
    {
        byte[] qoi = Qoi.Write(Image);
        Wire.WriteVarint(s, qoi.Length);
        s.Write(qoi);
    }
}
