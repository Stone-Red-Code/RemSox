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
}
