using System.Drawing;
using System.Text;

namespace RemSox.UI.GUI.Rendering;

public class RenderCommand
{
    public required int WindowId { get; set; }
    public required int ElementId { get; set; }
    public required RenderCommandType Type { get; set; }
    public Point Position { get; set; }
    public Dictionary<string, object?> Properties { get; set; } = [];

    public byte[] ToBytes()
    {
        using MemoryStream ms = new();
        Write(ms);
        return ms.ToArray();
    }

    public void Write(Stream s)
    {
        s.WriteByte((byte)Type);
        WriteVarint(s, WindowId);
        WriteVarint(s, ElementId);

        switch (Type)
        {
            case RenderCommandType.CreateWindow:
                WriteInt16(s, Position.X);
                WriteInt16(s, Position.Y);
                Size size = GetProp("Size", new Size(160, 120));
                WriteUInt16(s, size.Width);
                WriteUInt16(s, size.Height);
                WriteVarint(s, GetProp("ZIndex", 0));
                break;

            case RenderCommandType.DestroyWindow:
                break;

            case RenderCommandType.MoveWindow:
                WriteInt16(s, Position.X);
                WriteInt16(s, Position.Y);
                break;

            case RenderCommandType.DrawFilledRect:
            case RenderCommandType.DrawRectBorder:
                WriteInt16(s, Position.X);
                WriteInt16(s, Position.Y);
                Size rs = GetProp("Size", new Size(10, 10));
                WriteUInt16(s, rs.Width);
                WriteUInt16(s, rs.Height);
                WriteColor(s, GetProp("Color", Color.White));
                break;

            case RenderCommandType.DrawFilledCircle:
            case RenderCommandType.DrawCircle:
                WriteInt16(s, Position.X);
                WriteInt16(s, Position.Y);
                WriteUInt16(s, GetProp("Radius", 10));
                WriteColor(s, GetProp("Color", Color.White));
                break;

            case RenderCommandType.DrawPoint:
                WriteInt16(s, Position.X);
                WriteInt16(s, Position.Y);
                WriteColor(s, GetProp("Color", Color.White));
                break;

            case RenderCommandType.DrawText:
                WriteInt16(s, Position.X);
                WriteInt16(s, Position.Y);
                WriteColor(s, GetProp("Color", Color.White));
                s.WriteByte((byte)GetProp("FontSize", 12));
                string text = GetProp("Content", string.Empty);
                byte[] utf8 = Encoding.UTF8.GetBytes(text);
                WriteVarint(s, utf8.Length);
                s.Write(utf8, 0, utf8.Length);
                WriteVarint(s, GetProp("MaxWidth", int.MaxValue));
                break;

            case RenderCommandType.DrawLine:
                WriteInt16(s, Position.X);
                WriteInt16(s, Position.Y);
                Point end = GetProp("EndPosition", Position);
                WriteInt16(s, end.X);
                WriteInt16(s, end.Y);
                WriteColor(s, GetProp("Color", Color.White));
                break;

            case RenderCommandType.RemovePrimitives:
                break;

            case RenderCommandType.SetCursor:
                WriteInt16(s, Position.X);
                WriteInt16(s, Position.Y);
                break;

            case RenderCommandType.ScreenInfo:
                WriteUInt16(s, GetProp("Width", 0));
                WriteUInt16(s, GetProp("Height", 0));
                break;
        }
    }

    public static RenderCommand FromBytes(byte[] data)
    {
        int offset = 0;
        RenderCommandType type = (RenderCommandType)data[offset++];
        int windowId = ReadVarint(data, ref offset);
        int elementId = ReadVarint(data, ref offset);
        Point pos = default;
        Dictionary<string, object?> props = [];

        switch (type)
        {
            case RenderCommandType.CreateWindow:
                pos = new Point(ReadInt16(data, ref offset), ReadInt16(data, ref offset));
                props["Size"] = new Size(ReadUInt16(data, ref offset), ReadUInt16(data, ref offset));
                props["ZIndex"] = ReadVarint(data, ref offset);
                break;

            case RenderCommandType.DestroyWindow:
                break;

            case RenderCommandType.MoveWindow:
                pos = new Point(ReadInt16(data, ref offset), ReadInt16(data, ref offset));
                break;

            case RenderCommandType.DrawFilledRect:
            case RenderCommandType.DrawRectBorder:
                pos = new Point(ReadInt16(data, ref offset), ReadInt16(data, ref offset));
                props["Size"] = new Size(ReadUInt16(data, ref offset), ReadUInt16(data, ref offset));
                props["Color"] = ReadColor(data, ref offset);
                break;

            case RenderCommandType.DrawFilledCircle:
            case RenderCommandType.DrawCircle:
                pos = new Point(ReadInt16(data, ref offset), ReadInt16(data, ref offset));
                props["Radius"] = ReadUInt16(data, ref offset);
                props["Color"] = ReadColor(data, ref offset);
                break;

            case RenderCommandType.DrawPoint:
                pos = new Point(ReadInt16(data, ref offset), ReadInt16(data, ref offset));
                props["Color"] = ReadColor(data, ref offset);
                break;

            case RenderCommandType.DrawText:
                pos = new Point(ReadInt16(data, ref offset), ReadInt16(data, ref offset));
                props["Color"] = ReadColor(data, ref offset);
                props["FontSize"] = data[offset++];
                int textLen = ReadVarint(data, ref offset);
                props["Content"] = Encoding.UTF8.GetString(data, offset, textLen);
                offset += textLen;
                props["MaxWidth"] = ReadVarint(data, ref offset);
                break;

            case RenderCommandType.DrawLine:
                pos = new Point(ReadInt16(data, ref offset), ReadInt16(data, ref offset));
                props["EndPosition"] = new Point(ReadInt16(data, ref offset), ReadInt16(data, ref offset));
                props["Color"] = ReadColor(data, ref offset);
                break;

            case RenderCommandType.RemovePrimitives:
                break;

            case RenderCommandType.SetCursor:
                pos = new Point(ReadInt16(data, ref offset), ReadInt16(data, ref offset));
                break;

            case RenderCommandType.ScreenInfo:
                props["Width"] = ReadUInt16(data, ref offset);
                props["Height"] = ReadUInt16(data, ref offset);
                break;
        }

        return new RenderCommand
        {
            Type = type,
            WindowId = windowId,
            ElementId = elementId,
            Position = pos,
            Properties = props,
        };
    }

    // --- Serialization helpers ---

    private T GetProp<T>(string key, T fallback)
    {
        return Properties.TryGetValue(key, out object? raw) && raw is T val ? val : fallback;
    }

    private static void WriteVarint(Stream s, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            s.WriteByte((byte)(v | 0x80));
            v >>= 7;
        }
        s.WriteByte((byte)v);
    }

    private static void WriteInt16(Stream s, int value)
    {
        s.WriteByte((byte)(value & 0xFF));
        s.WriteByte((byte)((value >> 8) & 0xFF));
    }

    private static void WriteUInt16(Stream s, int value)
    {
        s.WriteByte((byte)(value & 0xFF));
        s.WriteByte((byte)((value >> 8) & 0xFF));
    }

    private static void WriteColor(Stream s, Color c)
    {
        s.WriteByte(c.R);
        s.WriteByte(c.G);
        s.WriteByte(c.B);
    }

    private static int ReadVarint(byte[] data, ref int offset)
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
        }
    }

    private static int ReadInt16(byte[] data, ref int offset)
    {
        int lo = data[offset++];
        int hi = data[offset++];
        return (short)(lo | (hi << 8));
    }

    private static int ReadUInt16(byte[] data, ref int offset)
    {
        int lo = data[offset++];
        int hi = data[offset++];
        return lo | (hi << 8);
    }

    private static Color ReadColor(byte[] data, ref int offset)
    {
        byte r = data[offset++];
        byte g = data[offset++];
        byte b = data[offset++];
        return Color.FromArgb(r, g, b);
    }
}