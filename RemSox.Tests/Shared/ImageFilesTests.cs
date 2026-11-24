using System.Drawing;

using RemSox.Shared.Imaging;

namespace RemSox.Tests.Shared;

/// <summary>Reading BMP and cursor files.</summary>
public sealed class ImageFilesTests
{
    private const uint Red = 0xFFFF0000u;
    private const uint Green = 0xFF00FF00u;
    private const uint Blue = 0xFF0000FFu;
    private const uint White = 0xFFFFFFFFu;

    [Fact]
    public void BottomUp24BitBmpIsReadTopRowFirst()
    {
        // Rows are stored bottom-up as BGR, each padded to 4 bytes.
        byte[] rows =
        [
            255, 0, 0, 255, 255, 255, 0, 0,    // bottom: blue, white
            0, 0, 255, 0, 255, 0, 0, 0,        // top: red, green
        ];

        PixelImage image = ImageFiles.ReadBmp(Bmp(2, 2, 24, [], rows));

        Assert.Equal([Red, Green, Blue, White], image.Pixels);
    }

    [Fact]
    public void TopDownBmpKeepsItsRowOrder()
    {
        byte[] rows = [0, 0, 255, 0, 255, 0, 0, 0];

        PixelImage image = ImageFiles.ReadBmp(Bmp(2, -1, 24, [], rows));

        Assert.Equal([Red, Green], image.Pixels);
    }

    [Fact]
    public void PalettedBmpLooksUpItsColours()
    {
        byte[] palette = [0, 0, 255, 0, 255, 0, 0, 0];       // 0 red, 1 blue
        byte[] rows = [0b0110_0000, 0, 0, 0];                // 1 bit per pixel: 0, 1, 1

        PixelImage image = ImageFiles.ReadBmp(Bmp(3, 1, 1, palette, rows));

        Assert.Equal([Red, Blue, Blue], image.Pixels);
    }

    [Fact]
    public void ThirtyTwoBitBmpWithoutAlphaIsOpaque()
    {
        PixelImage image = ImageFiles.ReadBmp(Bmp(1, 1, 32, [], [0, 255, 0, 0]));

        Assert.Equal([Green], image.Pixels);
    }

    [Fact]
    public void CursorKeepsAlphaAndHotspot()
    {
        // 2x1, 32-bit BGRA: a half-transparent red pixel, then a transparent one.
        byte[] file = Cursor(2, 1, new Point(1, 0), 32, [0, 0, 255, 128, 0, 0, 0, 0], mask: [0, 0, 0, 0]);

        (PixelImage image, Point hotspot) = ImageFiles.ReadCursor(file);

        Assert.Equal([0x80FF0000u, 0u], image.Pixels);
        Assert.Equal(new Point(1, 0), hotspot);
    }

    [Fact]
    public void CursorWithoutAlphaTakesTransparencyFromItsMask()
    {
        byte[] file = Cursor(2, 1, Point.Empty, 24, [255, 255, 255, 255, 255, 255, 0, 0], mask: [0b0100_0000, 0, 0, 0]);

        (PixelImage image, _) = ImageFiles.ReadCursor(file);

        Assert.Equal([White, 0u], image.Pixels);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { (byte)'B', (byte)'M', 0, 0 })]
    [InlineData(new byte[] { 0, 0, 2, 0, 1, 0 })]
    public void DamagedFilesAreRefused(byte[] file)
    {
        _ = Assert.Throws<InvalidDataException>(() => ImageFiles.ReadBmp(file));
        _ = Assert.Throws<InvalidDataException>(() => ImageFiles.ReadCursor(file));
    }

    /// <summary>A BITMAPINFOHEADER for a DIB, with the height as stored.</summary>
    private static byte[] InfoHeader(int width, int height, int bitCount)
    {
        using MemoryStream s = new();
        using BinaryWriter w = new(s);
        w.Write(40);
        w.Write(width);
        w.Write(height);
        w.Write((short)1);
        w.Write((short)bitCount);
        w.Write(new byte[24]);
        return s.ToArray();
    }

    private static byte[] Bmp(int width, int height, int bitCount, byte[] palette, byte[] rows)
    {
        int pixelOffset = 14 + 40 + palette.Length;
        using MemoryStream s = new();
        using BinaryWriter w = new(s);
        w.Write((byte)'B');
        w.Write((byte)'M');
        w.Write(pixelOffset + rows.Length);
        w.Write(0);
        w.Write(pixelOffset);
        w.Write(InfoHeader(width, height, bitCount));
        w.Write(palette);
        w.Write(rows);
        return s.ToArray();
    }

    private static byte[] Cursor(int width, int height, Point hotspot, int bitCount, byte[] rows, byte[] mask)
    {
        byte[] image = [.. InfoHeader(width, height * 2, bitCount), .. rows, .. mask];
        using MemoryStream s = new();
        using BinaryWriter w = new(s);
        w.Write((short)0);
        w.Write((short)2);
        w.Write((short)1);
        w.Write((byte)width);
        w.Write((byte)height);
        w.Write((short)0);
        w.Write((short)hotspot.X);
        w.Write((short)hotspot.Y);
        w.Write(image.Length);
        w.Write(6 + 16);
        w.Write(image);
        return s.ToArray();
    }
}
