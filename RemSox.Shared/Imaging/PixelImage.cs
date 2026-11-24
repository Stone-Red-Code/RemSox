namespace RemSox.Shared.Imaging;

/// <summary>
/// An image as both the kernel and the viewer draw it: ARGB pixels (not premultiplied), row by
/// row from the top left. Cosmos has no image types, so the decoders here make these.
/// </summary>
public sealed class PixelImage
{
    /// <summary>The largest width or height an image file may say it has, so a damaged file cannot ask for gigabytes.</summary>
    public const int MaxSide = 16384;

    public PixelImage(int width, int height, uint[] pixels)
    {
        if (width <= 0 || height <= 0 || width > MaxSide || height > MaxSide)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"An image is 1 to {MaxSide} pixels on each side, not {width}x{height}.");
        }

        if (pixels.Length != width * height)
        {
            throw new ArgumentException($"A {width}x{height} image has {width * height} pixels, not {pixels.Length}.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public uint[] Pixels { get; }

    /// <summary>
    /// Returns the image scaled to cover <paramref name="width"/> by <paramref name="height"/>
    /// keeping its proportions, the overflow cropped evenly from both sides, as a wallpaper fills a
    /// screen. Nearest-neighbour, which is plenty for an image drawn once at its final size.
    /// </summary>
    public PixelImage ScaleToFill(int width, int height)
    {
        if (width == Width && height == Height)
        {
            return this;
        }

        // The part of the source shown: the screen's proportions, as large as fits, centred.
        long cropWidth = Width;
        long cropHeight = Height;
        if ((long)Width * height > (long)Height * width)
        {
            cropWidth = (long)Height * width / height;
        }
        else
        {
            cropHeight = (long)Width * height / width;
        }

        long left = (Width - cropWidth) / 2;
        long top = (Height - cropHeight) / 2;

        int[] columns = new int[width];
        for (int x = 0; x < width; x++)
        {
            columns[x] = (int)(left + (((2L * x) + 1) * cropWidth / (2L * width)));
        }

        uint[] pixels = new uint[width * height];
        for (int y = 0; y < height; y++)
        {
            int row = (int)(top + (((2L * y) + 1) * cropHeight / (2L * height))) * Width;
            int target = y * width;
            for (int x = 0; x < width; x++)
            {
                pixels[target + x] = Pixels[row + columns[x]];
            }
        }

        return new PixelImage(width, height, pixels);
    }
}
