using System.Drawing;
using System.Text;

using RemSox.Shared.Imaging;

namespace RemSox.Shared.UI.GUI.Rendering;

/// <summary>
/// One thing a screen tells its renderers: a change to the screen or a window
/// (<see cref="ScreenInfoCommand"/>, <see cref="CreateWindowCommand"/>, ...) or a drawing
/// primitive (<see cref="Primitive"/>). Each kind is a class of its own with typed values, and
/// writes and reads itself in the binary form the remote desktop sends.
/// </summary>
/// <remarks>
/// On the wire: the type byte, the window id and element id (varints), then what the kind
/// carries (<see cref="WriteBody"/>). <see cref="FromBytes"/> picks the class by the type byte.
/// </remarks>
public abstract class RenderCommand
{
    public required int WindowId { get; init; }

    /// <summary>Gets the element (for a primitive, its primitive id) or window the command is about.</summary>
    public required int ElementId { get; init; }

    public abstract RenderCommandType Type { get; }

    public byte[] ToBytes()
    {
        using MemoryStream ms = new();
        Write(ms);
        return ms.ToArray();
    }

    public void Write(Stream s)
    {
        s.WriteByte((byte)Type);
        Wire.WriteVarint(s, WindowId);
        Wire.WriteVarint(s, ElementId);
        WriteBody(s);
    }

    /// <summary>Writes what this kind of command carries after the common header.</summary>
    protected abstract void WriteBody(Stream s);

    /// <exception cref="InvalidDataException">Not a command this version knows, or cut short.</exception>
    public static RenderCommand FromBytes(byte[] data)
    {
        try
        {
            Wire.Reader r = new(data);
            RenderCommandType type = (RenderCommandType)r.Byte();
            int windowId = r.Varint();
            int elementId = r.Varint();

            return type switch
            {
                RenderCommandType.ScreenInfo => new ScreenInfoCommand { WindowId = windowId, ElementId = elementId, Size = new Size(r.UInt16(), r.UInt16()) },
                RenderCommandType.SetCursor => new SetCursorCommand { WindowId = windowId, ElementId = elementId, Position = r.Point() },
                RenderCommandType.SetCursorShape => SetCursorShapeCommand.Read(windowId, elementId, r),
                RenderCommandType.SetBackground => SetBackgroundCommand.Read(windowId, elementId, r),
                RenderCommandType.CreateWindow => new CreateWindowCommand
                {
                    WindowId = windowId,
                    ElementId = elementId,
                    Position = r.Point(),
                    Size = new Size(r.UInt16(), r.UInt16()),
                    ZIndex = r.Varint(),
                    Layer = (WindowLayer)r.Byte(),
                    CornerRadius = r.Byte(),
                },
                RenderCommandType.DestroyWindow => new DestroyWindowCommand { WindowId = windowId, ElementId = elementId },
                RenderCommandType.MoveWindow => new MoveWindowCommand { WindowId = windowId, ElementId = elementId, Position = r.Point() },
                RenderCommandType.SetWindowOpacity => new SetWindowOpacityCommand { WindowId = windowId, ElementId = elementId, Opacity = r.Byte() },
                RenderCommandType.RemovePrimitives => new RemovePrimitivesCommand { WindowId = windowId, ElementId = elementId },
                _ when IsDrawType(type) => Primitive.Read(type, windowId, elementId, r),
                _ => throw new InvalidDataException($"Unknown render command {(int)type}."),
            };
        }
        catch (IndexOutOfRangeException ex)
        {
            throw new InvalidDataException("A render command was cut short.", ex);
        }
    }

    /// <summary>Returns whether commands of <paramref name="type"/> draw something.</summary>
    public static bool IsDrawType(RenderCommandType type)
    {
        return type is >= RenderCommandType.DrawFilledRect and <= (RenderCommandType)0x3F;
    }
}

/// <summary>The size of the screen the windows are on.</summary>
public sealed class ScreenInfoCommand : RenderCommand
{
    public override RenderCommandType Type => RenderCommandType.ScreenInfo;

    public Size Size { get; init; }

    protected override void WriteBody(Stream s)
    {
        Wire.WriteUInt16(s, Size.Width);
        Wire.WriteUInt16(s, Size.Height);
    }
}

/// <summary>Where the pointer is, in screen pixels.</summary>
public sealed class SetCursorCommand : RenderCommand
{
    public override RenderCommandType Type => RenderCommandType.SetCursor;

    public Point Position { get; init; }

    protected override void WriteBody(Stream s)
    {
        Wire.WritePoint(s, Position);
    }
}

/// <summary>
/// The pointer's image and hotspot. Sent when a screen starts showing on a display and when the
/// cursor changes; its pixels go raw (a 32x32 cursor is 4 KB).
/// </summary>
public sealed class SetCursorShapeCommand : RenderCommand
{
    public override RenderCommandType Type => RenderCommandType.SetCursorShape;

    public required CursorShape Shape { get; init; }

    protected override void WriteBody(Stream s)
    {
        Wire.WritePoint(s, Shape.Hotspot);
        Wire.WriteUInt16(s, Shape.Width);
        Wire.WriteUInt16(s, Shape.Height);
        foreach (uint pixel in Shape.Image.Pixels)
        {
            Wire.WriteUInt32(s, pixel);
        }
    }

    internal static SetCursorShapeCommand Read(int windowId, int elementId, Wire.Reader r)
    {
        Point hotspot = r.Point();
        int width = r.UInt16();
        int height = r.UInt16();
        if (width is 0 or > CursorShape.MaxSide || height is 0 or > CursorShape.MaxSide)
        {
            throw new InvalidDataException($"A cursor of {width}x{height} pixels.");
        }

        uint[] pixels = new uint[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = r.UInt32();
        }

        try
        {
            return new SetCursorShapeCommand { WindowId = windowId, ElementId = elementId, Shape = new CursorShape(new PixelImage(width, height, pixels), hotspot) };
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }
}

/// <summary>
/// What is drawn behind the windows: a colour, and an image already scaled to the screen
/// (a wallpaper, a lock screen) or none. Sent when a screen starts showing on a display and when
/// its background changes; the image goes as QOI (<see cref="Qoi"/>), a fraction of its pixels'
/// size.
/// </summary>
public sealed class SetBackgroundCommand : RenderCommand
{
    public override RenderCommandType Type => RenderCommandType.SetBackground;

    /// <summary>Gets the colour where there is no image.</summary>
    public Color Color { get; init; }

    /// <summary>Gets the image, the size of the screen, or null for the colour alone.</summary>
    public PixelImage? Image { get; init; }

    protected override void WriteBody(Stream s)
    {
        Wire.WriteColor(s, Color);
        if (Image is null)
        {
            s.WriteByte(0);
            return;
        }

        s.WriteByte(1);
        byte[] qoi = Qoi.Write(Image);
        Wire.WriteVarint(s, qoi.Length);
        s.Write(qoi);
    }

    internal static SetBackgroundCommand Read(int windowId, int elementId, Wire.Reader r)
    {
        Color color = r.Color();
        PixelImage? image = r.Byte() == 0 ? null : Qoi.Read(r.Bytes());
        return new SetBackgroundCommand { WindowId = windowId, ElementId = elementId, Color = color, Image = image };
    }
}

/// <summary>Creates a window, or changes its size or stacking (keeping its primitives).</summary>
public sealed class CreateWindowCommand : RenderCommand
{
    public override RenderCommandType Type => RenderCommandType.CreateWindow;

    public Point Position { get; init; }

    public Size Size { get; init; }

    /// <summary>Gets where the window is stacked within its <see cref="Layer"/>: higher over lower.</summary>
    public int ZIndex { get; init; }

    public WindowLayer Layer { get; init; } = WindowLayer.Normal;

    /// <summary>
    /// Gets the radius of the window's corners, 0-255: what lies outside them is not composited,
    /// so what is under the window shows there. 0 for square corners (a maximized window, the
    /// taskbar).
    /// </summary>
    public int CornerRadius { get; init; }

    protected override void WriteBody(Stream s)
    {
        Wire.WritePoint(s, Position);
        Wire.WriteUInt16(s, Size.Width);
        Wire.WriteUInt16(s, Size.Height);
        Wire.WriteVarint(s, ZIndex);
        s.WriteByte((byte)Layer);
        s.WriteByte((byte)Math.Clamp(CornerRadius, 0, 255));
    }
}

public sealed class DestroyWindowCommand : RenderCommand
{
    public override RenderCommandType Type => RenderCommandType.DestroyWindow;

    protected override void WriteBody(Stream s)
    {
    }
}

public sealed class MoveWindowCommand : RenderCommand
{
    public override RenderCommandType Type => RenderCommandType.MoveWindow;

    public Point Position { get; init; }

    protected override void WriteBody(Stream s)
    {
        Wire.WritePoint(s, Position);
    }
}

/// <summary>How opaque a window is composited, 0-255 (less while it is dragged).</summary>
public sealed class SetWindowOpacityCommand : RenderCommand
{
    public override RenderCommandType Type => RenderCommandType.SetWindowOpacity;

    public byte Opacity { get; init; } = 255;

    protected override void WriteBody(Stream s)
    {
        s.WriteByte(Opacity);
    }
}

/// <summary>
/// Removes every primitive of the element <see cref="RenderCommand.ElementId"/> (for a negative
/// id, a part of the window's frame, the one primitive with that id).
/// </summary>
public sealed class RemovePrimitivesCommand : RenderCommand
{
    public override RenderCommandType Type => RenderCommandType.RemovePrimitives;

    protected override void WriteBody(Stream s)
    {
    }
}

/// <summary>The binary encoding the commands share: varints, little-endian 16-bit numbers, RGB colors.</summary>
internal static class Wire
{
    public static void WriteVarint(Stream s, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            s.WriteByte((byte)(v | 0x80));
            v >>= 7;
        }

        s.WriteByte((byte)v);
    }

    public static void WriteInt16(Stream s, int value)
    {
        s.WriteByte((byte)(value & 0xFF));
        s.WriteByte((byte)((value >> 8) & 0xFF));
    }

    public static void WriteUInt16(Stream s, int value)
    {
        WriteInt16(s, value);
    }

    public static void WriteUInt32(Stream s, uint value)
    {
        WriteInt16(s, (int)(value & 0xFFFF));
        WriteInt16(s, (int)(value >> 16));
    }

    public static void WritePoint(Stream s, Point p)
    {
        WriteInt16(s, p.X);
        WriteInt16(s, p.Y);
    }

    /// <summary>Writes a colour as four bytes, alpha first, so a translucent one stays translucent.</summary>
    public static void WriteColor(Stream s, Color c)
    {
        s.WriteByte(c.A);
        s.WriteByte(c.R);
        s.WriteByte(c.G);
        s.WriteByte(c.B);
    }

    public static void WriteString(Stream s, string text)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        WriteVarint(s, utf8.Length);
        s.Write(utf8, 0, utf8.Length);
    }

    /// <summary>Reads what the Write methods wrote, in order.</summary>
    public sealed class Reader(byte[] data)
    {
        private int offset;

        public byte Byte()
        {
            return data[offset++];
        }

        public int Varint()
        {
            uint result = 0;
            int shift = 0;
            while (true)
            {
                byte b = data[offset++];
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return (int)result;
                }

                shift += 7;
                if (shift > 28)
                {
                    throw new InvalidDataException("A varint is too long.");
                }
            }
        }

        public int Int16()
        {
            int lo = data[offset++];
            int hi = data[offset++];
            return (short)(lo | (hi << 8));
        }

        public int UInt16()
        {
            int lo = data[offset++];
            int hi = data[offset++];
            return lo | (hi << 8);
        }

        public uint UInt32()
        {
            return (uint)UInt16() | ((uint)UInt16() << 16);
        }

        public Point Point()
        {
            return new Point(Int16(), Int16());
        }

        public Color Color()
        {
            return System.Drawing.Color.FromArgb(data[offset++], data[offset++], data[offset++], data[offset++]);
        }

        /// <summary>Reads a length-prefixed run of bytes.</summary>
        public byte[] Bytes()
        {
            int length = Varint();
            if (length < 0 || length > data.Length - offset)
            {
                throw new InvalidDataException("Bytes run past the end of their command.");
            }

            byte[] bytes = data[offset..(offset + length)];
            offset += length;
            return bytes;
        }

        public string String()
        {
            int length = Varint();
            if (length < 0 || length > data.Length - offset)
            {
                throw new InvalidDataException("A string runs past the end of its command.");
            }

            string text = Encoding.UTF8.GetString(data, offset, length);
            offset += length;
            return text;
        }
    }
}
