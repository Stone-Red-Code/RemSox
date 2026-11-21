using System.Buffers.Binary;
using System.Text;

namespace RemSox.Shared.Networking;

/// <summary>
/// Message types and payload encoding for input sent from a remote desktop viewer to the server.
/// All integers are little-endian.
/// </summary>
public static class RemoteInputProtocol
{
    public const string SyncRequest = "SyncRequest";
    public const string MouseMove = "MouseMove";
    public const string MouseDown = "MouseDown";
    public const string MouseUp = "MouseUp";
    public const string MouseWheel = "MouseWheel";
    public const string KeyEvent = "KeyEvent";

    /// <summary>Several serialized render commands in one message, applied in order.</summary>
    public const string RenderBatch = "RenderBatch";

    /// <summary>
    /// The server's last message before it closes the connection on purpose, with the reason in
    /// UTF-8 (the session was logged out, or taken back at the machine). The viewer must not
    /// reconnect by itself then: that would take the session back from whoever just got it.
    /// </summary>
    public const string SessionEnded = "SessionEnded";

    /// <summary>
    /// Packs serialized render commands as a sequence of [int32 length][command bytes].
    /// </summary>
    public static byte[] SerializeRenderBatch(IReadOnlyList<byte[]> commands)
    {
        int size = 0;
        foreach (byte[] command in commands)
        {
            size += 4 + command.Length;
        }

        byte[] data = new byte[size];
        int offset = 0;
        foreach (byte[] command in commands)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), command.Length);
            command.CopyTo(data, offset + 4);
            offset += 4 + command.Length;
        }

        return data;
    }

    public static List<byte[]> DeserializeRenderBatch(byte[] data)
    {
        List<byte[]> commands = [];
        int offset = 0;

        while (offset + 4 <= data.Length)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
            offset += 4;

            if (length < 0 || offset + length > data.Length)
            {
                break;
            }

            commands.Add(data.AsSpan(offset, length).ToArray());
            offset += length;
        }

        return commands;
    }

    public static byte[] SerializeMouseMove(int x, int y)
    {
        byte[] data = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(data, x);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), y);
        return data;
    }

    public static (int X, int Y) DeserializeMouseMove(byte[] data)
    {
        return (BinaryPrimitives.ReadInt32LittleEndian(data), BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)));
    }

    /// <param name="button">"Left", "Right" or "Middle".</param>
    public static byte[] SerializeMouseButton(int x, int y, string button)
    {
        byte[] buttonBytes = Encoding.UTF8.GetBytes(button);
        byte[] data = new byte[12 + buttonBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(data, x);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), y);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), buttonBytes.Length);
        buttonBytes.CopyTo(data, 12);
        return data;
    }

    public static (int X, int Y, string Button) DeserializeMouseButton(byte[] data)
    {
        int x = BinaryPrimitives.ReadInt32LittleEndian(data);
        int y = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        int length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8));
        return (x, y, Encoding.UTF8.GetString(data, 12, length));
    }

    /// <param name="delta">Wheel notches; positive scrolls down, like a PS/2 mouse.</param>
    public static byte[] SerializeMouseWheel(int x, int y, int delta)
    {
        byte[] data = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(data, x);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), y);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), delta);
        return data;
    }

    public static (int X, int Y, int Delta) DeserializeMouseWheel(byte[] data)
    {
        return (BinaryPrimitives.ReadInt32LittleEndian(data),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8)));
    }

    /// <param name="key">A Cosmos ConsoleKeyEx value.</param>
    /// <param name="keyChar">The typed character, or empty for keys that produce none.</param>
    public static byte[] SerializeKeyEvent(int key, string keyChar, bool shift, bool alt, bool control, bool pressed)
    {
        byte[] charBytes = Encoding.UTF8.GetBytes(keyChar);
        byte[] data = new byte[8 + charBytes.Length + 4];
        BinaryPrimitives.WriteInt32LittleEndian(data, key);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), charBytes.Length);
        charBytes.CopyTo(data, 8);
        int offset = 8 + charBytes.Length;
        data[offset] = shift ? (byte)1 : (byte)0;
        data[offset + 1] = alt ? (byte)1 : (byte)0;
        data[offset + 2] = control ? (byte)1 : (byte)0;
        data[offset + 3] = pressed ? (byte)1 : (byte)0;
        return data;
    }

    public static (int Key, string KeyChar, bool Shift, bool Alt, bool Control, bool Pressed) DeserializeKeyEvent(byte[] data)
    {
        int key = BinaryPrimitives.ReadInt32LittleEndian(data);
        int charLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        string keyChar = Encoding.UTF8.GetString(data, 8, charLength);
        int offset = 8 + charLength;
        return (key, keyChar, data[offset] != 0, data[offset + 1] != 0, data[offset + 2] != 0, data[offset + 3] != 0);
    }
}
