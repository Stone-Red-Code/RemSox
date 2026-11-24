using RemSox.Shared.Imaging;

namespace RemSox.Tests.Shared;

/// <summary>QOI images, and scaling an image to fill a screen.</summary>
public sealed class QoiTests
{
    [Fact]
    public void EveryKindOfPixelChangeSurvivesARoundTrip()
    {
        // Runs, small and larger changes, a colour seen before, an alpha change, a wrap past 255.
        uint[] pixels =
        [
            0xFF000000, 0xFF000000, 0xFF000000, 0xFF010101, 0xFF203040, 0xFF263040,
            0xFFFFFFFF, 0xFF010101, 0x80203040, 0xFF00FF00, 0xFF02FF01, 0xFFFF0000,
        ];
        PixelImage image = new(4, 3, pixels);

        PixelImage read = Qoi.Read(Qoi.Write(image));

        Assert.Equal(4, read.Width);
        Assert.Equal(3, read.Height);
        Assert.Equal(pixels, read.Pixels);
    }

    [Fact]
    public void LongRunsAndGradientsCompress()
    {
        uint[] pixels = new uint[256 * 64];
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 256; x++)
            {
                pixels[(y * 256) + x] = 0xFF000000u | (uint)(y << 16) | (uint)(x / 8 << 8);
            }
        }

        PixelImage image = new(256, 64, pixels);
        byte[] file = Qoi.Write(image);

        // Runs of 8 equal pixels: a run byte and a difference byte each, under a tenth of the raw 4 bytes a pixel.
        Assert.True(file.Length < pixels.Length * 4 / 10, $"{file.Length} bytes");
        Assert.Equal(pixels, Qoi.Read(file).Pixels);
    }

    [Fact]
    public void AFileMadeByTheReferenceEncoderReads()
    {
        // 2x1, 3 channels: QOI_OP_RGB (10, 20, 30), then QOI_OP_DIFF (+1, -1, 0), then the end.
        byte[] file =
        [
            (byte)'q', (byte)'o', (byte)'i', (byte)'f', 0, 0, 0, 2, 0, 0, 0, 1, 3, 0,
            0xFE, 10, 20, 30,
            0x40 | (3 << 4) | (1 << 2) | 2,
            0, 0, 0, 0, 0, 0, 0, 1,
        ];

        Assert.Equal([0xFF0A141Eu, 0xFF0B131Eu], ImageFiles.Read(file).Pixels);
    }

    [Theory]
    [InlineData(new byte[] { (byte)'q', (byte)'o', (byte)'i', (byte)'f', 0, 0, 0, 2, 0, 0, 0, 2, 4, 0, 0xFE, 1, 2, 3 })]
    [InlineData(new byte[] { (byte)'q', (byte)'o', (byte)'i', (byte)'f', 0xFF, 0, 0, 0, 0, 0, 0, 1, 4, 0, 0, 0, 0, 0, 0, 0, 0, 1 })]
    public void DamagedQoiIsRefused(byte[] file)
    {
        _ = Assert.Throws<InvalidDataException>(() => Qoi.Read(file));
    }

    [Fact]
    public void FillingCropsTheSidesOfAWiderImage()
    {
        // 4x1 into 2x1... the same proportions; 4x2 into 2x2 keeps the middle two columns.
        PixelImage wide = new(4, 2, [1, 2, 3, 4, 5, 6, 7, 8]);

        PixelImage filled = wide.ScaleToFill(2, 2);

        Assert.Equal([2u, 3u, 6u, 7u], filled.Pixels);
    }

    [Fact]
    public void FillingScalesUpWithoutCropping()
    {
        PixelImage image = new(2, 1, [1, 2]);

        Assert.Equal([1u, 1u, 2u, 2u, 1u, 1u, 2u, 2u], image.ScaleToFill(4, 2).Pixels);
    }
}
