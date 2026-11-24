using System.Drawing;

using RemSox.Shared.Imaging;
using RemSox.Shared.UI.GUI.Rendering;

namespace RemSox.Tests.Shared;

/// <summary>Reading BMP and cursor files, and sending a cursor to a viewer.</summary>
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

    [Fact]
    public void EveryCursorTheKernelShipsReads()
    {
        string[] files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Cursors"), "*.cur");
        Assert.Equal(21, files.Length);

        foreach (string file in files)
        {
            CursorShape shape = CursorShape.FromCursorFile(File.ReadAllBytes(file));

            Assert.Contains(shape.Image.Pixels, pixel => pixel >> 24 == 0xFF);
            Assert.Contains(shape.Image.Pixels, pixel => pixel >> 24 == 0);
        }
    }

    [Fact]
    public void TheArrowPointsWithItsTopLeft()
    {
        CursorShape arrow = CursorShape.FromCursorFile(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Cursors", "arrow.cur")));

        Assert.True(arrow.Hotspot.X < arrow.Width / 3 && arrow.Hotspot.Y < arrow.Height / 3);
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

    [Fact]
    public void CursorShapeGoesOverTheWireUnchanged()
    {
        CursorShape shape = new(new PixelImage(2, 1, [0x80FF0000u, 0u]), new Point(1, 0));
        byte[] data = new SetCursorShapeCommand { WindowId = 0, ElementId = 0, Shape = shape }.ToBytes();

        Scene scene = new();
        Assert.Equal(SceneChange.CursorShape, scene.Apply(RenderCommand.FromBytes(data)));
        Assert.Equal(shape.Image.Pixels, scene.CursorShape!.Image.Pixels);
        Assert.Equal(shape.Hotspot, scene.CursorShape.Hotspot);
    }

    [Fact]
    public void BoundsArePlacedByTheHotspot()
    {
        CursorShape shape = new(new PixelImage(3, 3, new uint[9]), new Point(1, 2));

        Assert.Equal(new Rectangle(9, 18, 3, 3), shape.GetBounds(new Point(10, 20)));
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
