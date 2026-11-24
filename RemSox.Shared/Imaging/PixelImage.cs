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
    /// Returns the image scaled down, keeping its proportions, to fit inside
    /// <paramref name="maxWidth"/> by <paramref name="maxHeight"/>; one that already fits is
    /// returned as it is, never enlarged (nearest-neighbour enlarging only shows its pixels).
    /// </summary>
    public PixelImage ScaleToFit(int maxWidth, int maxHeight)
    {
        maxWidth = Math.Max(1, maxWidth);
        maxHeight = Math.Max(1, maxHeight);
        if (Width <= maxWidth && Height <= maxHeight)
        {
            return this;
        }

        // The smaller of the two ratios, compared without fractions: width/W against height/H.
        bool widthLimits = (long)maxWidth * Height <= (long)maxHeight * Width;
        int width = widthLimits ? maxWidth : (int)Math.Max(1, (long)Width * maxHeight / Height);
        int height = widthLimits ? (int)Math.Max(1, (long)Height * maxWidth / Width) : maxHeight;
        return ScaleToFill(width, height);
    }

    /// <summary>
    /// Returns the image with its transparency laid over <paramref name="background"/> (ARGB),
    /// so every pixel is opaque; an image with none is returned as it is.
    /// </summary>
    public PixelImage Flatten(uint background)
    {
        if (Pixels.All(pixel => pixel >> 24 == 0xFF))
        {
            return this;
        }

        uint[] pixels = new uint[Pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            uint pixel = Pixels[i];
            uint weight = (pixel >> 24) + 1;   // 1..256, so 255 keeps the pixel exactly
            uint inverse = 256 - weight;
            uint redBlue = ((((pixel & 0xFF00FFu) * weight) + ((background & 0xFF00FFu) * inverse)) >> 8) & 0xFF00FFu;
            uint green = ((((pixel & 0x00FF00u) * weight) + ((background & 0x00FF00u) * inverse)) >> 8) & 0x00FF00u;
            pixels[i] = 0xFF000000u | redBlue | green;
        }

        return new PixelImage(Width, Height, pixels);
    }

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
