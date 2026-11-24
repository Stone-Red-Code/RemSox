using System.Drawing;

namespace RemSox.Shared.Imaging;

/// <summary>
/// Reads the image files RemSox uses: Windows bitmaps (<c>.bmp</c>) and cursors (<c>.cur</c>,
/// which add a hotspot). Both hold a DIB: uncompressed, 1, 4 or 8 bits per pixel with a palette,
/// or 24 or 32 bits; 32-bit images keep their alpha.
/// </summary>
public static class ImageFiles
{
    private const uint BiRgb = 0;
    private const uint BiBitfields = 3;
    private const uint BiAlphaBitfields = 6;

    /// <summary>Reads an image file of either kind a picture may come in, BMP or QOI (<see cref="Qoi"/>), telling them apart by their first bytes.</summary>
    /// <exception cref="InvalidDataException">Neither, or one of a kind this does not read.</exception>
    public static PixelImage Read(byte[] file)
    {
        return Qoi.IsQoi(file) ? Qoi.Read(file) : ReadBmp(file);
    }

    /// <summary>Reads a <c>.bmp</c> file.</summary>
    /// <exception cref="InvalidDataException">Not a bitmap, or one of a kind this does not read.</exception>
    public static PixelImage ReadBmp(byte[] file)
    {
        if (file.Length < 14 || file[0] != (byte)'B' || file[1] != (byte)'M')
        {
            throw new InvalidDataException("Not a BMP file.");
        }

        return ReadDib(file, 14, (int)U32(file, 10), isIcon: false);
    }

    /// <summary>
    /// Reads a <c>.cur</c> file: its largest image up to 64 pixels (else its smallest), with the
    /// hotspot that image says it points with.
    /// </summary>
    /// <exception cref="InvalidDataException">Not a cursor, or one of a kind this does not read (PNG inside).</exception>
    public static (PixelImage Image, Point Hotspot) ReadCursor(byte[] file)
    {
        if (file.Length < 6 || U16(file, 0) != 0 || U16(file, 2) != 2)
        {
            throw new InvalidDataException("Not a cursor (.cur) file.");
        }

        int count = U16(file, 4);
        int chosen = -1;
        int chosenSide = 0;
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + (i * 16);
            Check(file, entry, 16);
            int side = file[entry] == 0 ? 256 : file[entry];
            bool better = chosen < 0
                || (side <= 64 && (chosenSide > 64 || side > chosenSide))
                || (side > 64 && chosenSide > 64 && side < chosenSide);
            if (better)
            {
                chosen = entry;
                chosenSide = side;
            }
        }

        if (chosen < 0)
        {
            throw new InvalidDataException("The cursor file holds no image.");
        }

        Point hotspot = new(U16(file, chosen + 4), U16(file, chosen + 6));
        int size = (int)U32(file, chosen + 8);
        int offset = (int)U32(file, chosen + 12);
        Check(file, offset, size);

        if (size >= 8 && file[offset] == 0x89 && file[offset + 1] == (byte)'P' && file[offset + 2] == (byte)'N' && file[offset + 3] == (byte)'G')
        {
            throw new InvalidDataException("Cursors with a PNG image inside are not supported; save it as a 32-bit BMP cursor.");
        }

        PixelImage image = ReadDib(file, offset, pixelOffset: -1, isIcon: true);
        if (hotspot.X >= image.Width || hotspot.Y >= image.Height)
        {
            throw new InvalidDataException($"The cursor's hotspot {hotspot.X},{hotspot.Y} lies outside its {image.Width}x{image.Height} image.");
        }

        return (image, hotspot);
    }

    /// <summary>
    /// Reads a DIB: a header, a palette or bit masks, the pixel rows (bottom-up unless the height
    /// is negative), and in an icon or cursor a 1-bit transparency mask after them.
    /// </summary>
    /// <param name="pixelOffset">Where the rows start (a BMP says so); -1 for right after the palette or masks.</param>
    /// <param name="isIcon">An icon or cursor: its height counts the mask too, and the mask decides transparency unless the pixels have alpha.</param>
    private static PixelImage ReadDib(byte[] file, int header, int pixelOffset, bool isIcon)
    {
        Check(file, header, 40);
        int headerSize = (int)U32(file, header);
        int width = (int)U32(file, header + 4);
        int height = (int)U32(file, header + 8);
        int bitCount = U16(file, header + 14);
        uint compression = U32(file, header + 16);
        int colorsUsed = (int)U32(file, header + 32);

        if (headerSize < 40)
        {
            throw new InvalidDataException("Old OS/2 bitmaps are not supported.");
        }

        bool topDown = height < 0 && !isIcon;
        height = Math.Abs(height);
        if (isIcon)
        {
            height /= 2;
        }

        if (width <= 0 || height <= 0 || width > PixelImage.MaxSide || height > PixelImage.MaxSide)
        {
            throw new InvalidDataException($"An image of {width}x{height} pixels.");
        }

        if (bitCount is not (1 or 4 or 8 or 24 or 32))
        {
            throw new InvalidDataException($"Images with {bitCount} bits per pixel are not supported.");
        }

        // The channel masks of a 32-bit image: in the header from version 4 on, after a version-1
        // header otherwise; plain BI_RGB is the usual BGRA order.
        uint red = 0x00FF0000u;
        uint green = 0x0000FF00u;
        uint blue = 0x000000FFu;
        uint alpha = 0xFF000000u;
        int afterHeader = header + headerSize;
        if (compression is BiBitfields or BiAlphaBitfields)
        {
            if (bitCount != 32)
            {
                throw new InvalidDataException("Only 32-bit images may have bit masks.");
            }

            int masks = headerSize >= 52 ? header + 40 : afterHeader;
            Check(file, masks, 12);
            red = U32(file, masks);
            green = U32(file, masks + 4);
            blue = U32(file, masks + 8);
            alpha = headerSize >= 56 || compression == BiAlphaBitfields ? U32(file, masks + 12) : 0;
            if (headerSize < 52)
            {
                afterHeader += compression == BiAlphaBitfields ? 16 : 12;
            }
        }
        else if (compression != BiRgb)
        {
            throw new InvalidDataException("Compressed images are not supported.");
        }

        uint[] palette = [];
        if (bitCount <= 8)
        {
            int colors = colorsUsed is > 0 and <= 256 ? colorsUsed : 1 << bitCount;
            Check(file, afterHeader, colors * 4);
            palette = new uint[colors];
            for (int i = 0; i < colors; i++)
            {
                palette[i] = 0xFF000000u | (U32(file, afterHeader + (i * 4)) & 0x00FFFFFFu);
            }

            afterHeader += colors * 4;
        }

        int rows = pixelOffset >= 0 ? pixelOffset : afterHeader;
        int stride = Stride(width, bitCount);
        Check(file, rows, stride * height);

        uint[] pixels = new uint[width * height];
        bool anyAlpha = false;
        for (int y = 0; y < height; y++)
        {
            int row = rows + ((topDown ? y : height - 1 - y) * stride);
            for (int x = 0; x < width; x++)
            {
                uint pixel;
                if (bitCount == 32)
                {
                    uint value = U32(file, row + (x * 4));
                    pixel = (Channel(value, alpha) << 24) | (Channel(value, red) << 16) | (Channel(value, green) << 8) | Channel(value, blue);
                    anyAlpha |= (pixel & 0xFF000000u) != 0;
                }
                else if (bitCount == 24)
                {
                    int at = row + (x * 3);
                    pixel = 0xFF000000u | ((uint)file[at + 2] << 16) | ((uint)file[at + 1] << 8) | file[at];
                }
                else
                {
                    int bit = x * bitCount;
                    int index = (file[row + (bit / 8)] >> (8 - bitCount - (bit % 8))) & ((1 << bitCount) - 1);
                    if (index >= palette.Length)
                    {
                        throw new InvalidDataException("A pixel names a colour the palette does not have.");
                    }

                    pixel = palette[index];
                }

                pixels[(y * width) + x] = pixel;
            }
        }

        if (bitCount == 32 && !anyAlpha)
        {
            // Alpha all zero means the file has none (most 32-bit BMPs): opaque, unless a mask says otherwise.
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] |= 0xFF000000u;
            }
        }

        if (isIcon && !(bitCount == 32 && anyAlpha))
        {
            ApplyMask(file, rows + (stride * height), width, height, pixels);
        }

        return new PixelImage(width, height, pixels);
    }

    /// <summary>Makes the pixels an icon's 1-bit AND mask marks transparent.</summary>
    private static void ApplyMask(byte[] file, int mask, int width, int height, uint[] pixels)
    {
        int stride = Stride(width, 1);
        if (mask + (stride * height) > file.Length)
        {
            return; // No mask: everything stays opaque.
        }

        for (int y = 0; y < height; y++)
        {
            int row = mask + ((height - 1 - y) * stride);
            for (int x = 0; x < width; x++)
            {
                if ((file[row + (x / 8)] & (0x80 >> (x % 8))) != 0)
                {
                    pixels[(y * width) + x] = 0;
                }
            }
        }
    }

    private static int Stride(int width, int bitCount)
    {
        return ((width * bitCount) + 31) / 32 * 4;
    }

    /// <summary>The 8-bit value of the channel <paramref name="mask"/> picks out of <paramref name="value"/> (0 for no mask).</summary>
    private static uint Channel(uint value, uint mask)
    {
        if (mask == 0)
        {
            return 0;
        }

        int shift = 0;
        while (((mask >> shift) & 1) == 0)
        {
            shift++;
        }

        uint max = mask >> shift;
        uint channel = (value & mask) >> shift;
        return max == 0xFF ? channel : channel * 255 / max;
    }

    private static void Check(byte[] file, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > file.Length - length)
        {
            throw new InvalidDataException("The image file is cut short.");
        }
    }

    private static int U16(byte[] file, int offset)
    {
        Check(file, offset, 2);
        return file[offset] | (file[offset + 1] << 8);
    }

    private static uint U32(byte[] file, int offset)
    {
        Check(file, offset, 4);
        return file[offset] | ((uint)file[offset + 1] << 8) | ((uint)file[offset + 2] << 16) | ((uint)file[offset + 3] << 24);
    }
}
