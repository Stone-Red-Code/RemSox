namespace RemSox.Shared.Imaging;

/// <summary>
/// The Quite OK Image format (<c>.qoi</c>, https://qoiformat.org): lossless and simple enough to
/// read and write in a page of code, yet compressing smooth images such as wallpapers several
/// times over. RemSox ships its wallpapers in it and sends backgrounds to viewers in it, since a
/// full-screen image is megabytes uncompressed and a Cosmos TCP write waits a round trip per
/// 536 bytes.
/// </summary>
public static class Qoi
{
    private const byte OpIndex = 0x00;
    private const byte OpDiff = 0x40;
    private const byte OpLuma = 0x80;
    private const byte OpRun = 0xC0;
    private const byte OpRgb = 0xFE;
    private const byte OpRgba = 0xFF;
    private const byte Mask2 = 0xC0;
    private const int HeaderSize = 14;
    private static readonly byte[] EndMarker = [0, 0, 0, 0, 0, 0, 0, 1];

    /// <summary>Returns whether <paramref name="file"/> starts like a QOI image.</summary>
    public static bool IsQoi(byte[] file)
    {
        return file.Length >= HeaderSize && file[0] == (byte)'q' && file[1] == (byte)'o' && file[2] == (byte)'i' && file[3] == (byte)'f';
    }

    /// <exception cref="InvalidDataException">Not a QOI image, or a damaged one.</exception>
    public static PixelImage Read(byte[] file)
    {
        if (!IsQoi(file))
        {
            throw new InvalidDataException("Not a QOI image.");
        }

        long width = U32BigEndian(file, 4);
        long height = U32BigEndian(file, 8);
        if (width is <= 0 or > PixelImage.MaxSide || height is <= 0 or > PixelImage.MaxSide)
        {
            throw new InvalidDataException($"A QOI image of {width}x{height} pixels.");
        }

        uint[] pixels = new uint[width * height];
        uint[] index = new uint[64];
        uint pixel = 0xFF000000u; // r = g = b = 0, a = 255
        int run = 0;
        int at = HeaderSize;
        int end = file.Length - EndMarker.Length;

        for (int i = 0; i < pixels.Length; i++)
        {
            if (run > 0)
            {
                run--;
            }
            else
            {
                if (at >= end)
                {
                    throw new InvalidDataException("The QOI image is cut short.");
                }

                byte op = file[at++];
                if (op == OpRgb)
                {
                    Need(at + 3, end);
                    pixel = (pixel & 0xFF000000u) | ((uint)file[at] << 16) | ((uint)file[at + 1] << 8) | file[at + 2];
                    at += 3;
                }
                else if (op == OpRgba)
                {
                    Need(at + 4, end);
                    pixel = ((uint)file[at + 3] << 24) | ((uint)file[at] << 16) | ((uint)file[at + 1] << 8) | file[at + 2];
                    at += 4;
                }
                else if ((op & Mask2) == OpIndex)
                {
                    pixel = index[op];
                }
                else if ((op & Mask2) == OpDiff)
                {
                    pixel = With(pixel, ((op >> 4) & 3) - 2, ((op >> 2) & 3) - 2, (op & 3) - 2);
                }
                else if ((op & Mask2) == OpLuma)
                {
                    Need(at + 1, end);
                    int green = (op & 0x3F) - 32;
                    byte second = file[at++];
                    pixel = With(pixel, green - 8 + ((second >> 4) & 0x0F), green, green - 8 + (second & 0x0F));
                }
                else
                {
                    run = op & 0x3F;
                }

                index[Hash(pixel)] = pixel;
            }

            pixels[i] = pixel;
        }

        return new PixelImage((int)width, (int)height, pixels);
    }

    /// <summary>Writes <paramref name="image"/> as a QOI file (4 channels, sRGB).</summary>
    public static byte[] Write(PixelImage image)
    {
        using MemoryStream s = new();
        s.Write([(byte)'q', (byte)'o', (byte)'i', (byte)'f']);
        WriteU32BigEndian(s, (uint)image.Width);
        WriteU32BigEndian(s, (uint)image.Height);
        s.WriteByte(4);
        s.WriteByte(0);

        uint[] index = new uint[64];
        uint previous = 0xFF000000u;
        int run = 0;
        uint[] pixels = image.Pixels;

        for (int i = 0; i < pixels.Length; i++)
        {
            uint pixel = pixels[i];
            if (pixel == previous)
            {
                run++;
                if (run == 62 || i == pixels.Length - 1)
                {
                    s.WriteByte((byte)(OpRun | (run - 1)));
                    run = 0;
                }

                continue;
            }

            if (run > 0)
            {
                s.WriteByte((byte)(OpRun | (run - 1)));
                run = 0;
            }

            int hash = Hash(pixel);
            if (index[hash] == pixel)
            {
                s.WriteByte((byte)(OpIndex | hash));
            }
            else
            {
                index[hash] = pixel;
                if ((pixel & 0xFF000000u) == (previous & 0xFF000000u))
                {
                    int dr = (sbyte)(byte)((pixel >> 16) - (previous >> 16));
                    int dg = (sbyte)(byte)((pixel >> 8) - (previous >> 8));
                    int db = (sbyte)(byte)(pixel - previous);
                    int drg = dr - dg;
                    int dbg = db - dg;

                    if (dr is >= -2 and <= 1 && dg is >= -2 and <= 1 && db is >= -2 and <= 1)
                    {
                        s.WriteByte((byte)(OpDiff | ((dr + 2) << 4) | ((dg + 2) << 2) | (db + 2)));
                    }
                    else if (dg is >= -32 and <= 31 && drg is >= -8 and <= 7 && dbg is >= -8 and <= 7)
                    {
                        s.WriteByte((byte)(OpLuma | (dg + 32)));
                        s.WriteByte((byte)(((drg + 8) << 4) | (dbg + 8)));
                    }
                    else
                    {
                        s.Write([OpRgb, (byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel]);
                    }
                }
                else
                {
                    s.Write([OpRgba, (byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel, (byte)(pixel >> 24)]);
                }
            }

            previous = pixel;
        }

        s.Write(EndMarker);
        return s.ToArray();
    }

    /// <summary>The pixel with its red, green and blue changed by the given amounts, wrapping as the format says.</summary>
    private static uint With(uint pixel, int dr, int dg, int db)
    {
        uint r = (byte)((pixel >> 16) + dr);
        uint g = (byte)((pixel >> 8) + dg);
        uint b = (byte)(pixel + db);
        return (pixel & 0xFF000000u) | (r << 16) | (g << 8) | b;
    }

    private static int Hash(uint pixel)
    {
        uint r = (pixel >> 16) & 0xFF;
        uint g = (pixel >> 8) & 0xFF;
        uint b = pixel & 0xFF;
        uint a = pixel >> 24;
        return (int)(((r * 3) + (g * 5) + (b * 7) + (a * 11)) % 64);
    }

    private static void Need(int until, int end)
    {
        if (until > end)
        {
            throw new InvalidDataException("The QOI image is cut short.");
        }
    }

    private static long U32BigEndian(byte[] file, int at)
    {
        return ((long)file[at] << 24) | ((long)file[at + 1] << 16) | ((long)file[at + 2] << 8) | file[at + 3];
    }

    private static void WriteU32BigEndian(Stream s, uint value)
    {
        s.Write([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
    }
}
