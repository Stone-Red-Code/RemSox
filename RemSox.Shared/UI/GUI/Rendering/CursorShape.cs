using System.Drawing;

using RemSox.Shared.Imaging;

namespace RemSox.Shared.UI.GUI.Rendering;

/// <summary>
/// The pointer: an image (ARGB, with alpha) and its hotspot, the pixel that points (an arrow's
/// tip). It always comes from an image file, a <c>.cur</c>; the screen sends it to every
/// renderer (<see cref="SetCursorShapeCommand"/>), which draw it the same way.
/// </summary>
public sealed class CursorShape
{
    /// <summary>The largest cursor side: a cursor is redrawn on every move and goes to each viewer.</summary>
    public const int MaxSide = 256;

    public CursorShape(PixelImage image, Point hotspot)
    {
        if (image.Width > MaxSide || image.Height > MaxSide)
        {
            throw new ArgumentException($"A cursor is at most {MaxSide}x{MaxSide} pixels, not {image.Width}x{image.Height}.", nameof(image));
        }

        if (hotspot.X < 0 || hotspot.Y < 0 || hotspot.X >= image.Width || hotspot.Y >= image.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(hotspot), $"The hotspot {hotspot.X},{hotspot.Y} lies outside the {image.Width}x{image.Height} cursor.");
        }

        Image = image;
        Hotspot = hotspot;
    }

    public PixelImage Image { get; }

    public int Width => Image.Width;

    public int Height => Image.Height;

    /// <summary>Gets the pixel, from the image's top left, that is at the pointer's position.</summary>
    public Point Hotspot { get; }

    /// <summary>Reads a <c>.cur</c> file.</summary>
    /// <exception cref="InvalidDataException">Not a cursor this can read, or too big for one.</exception>
    public static CursorShape FromCursorFile(byte[] file)
    {
        (PixelImage image, Point hotspot) = ImageFiles.ReadCursor(file);
        if (image.Width > MaxSide || image.Height > MaxSide)
        {
            throw new InvalidDataException($"A cursor is at most {MaxSide}x{MaxSide} pixels, not {image.Width}x{image.Height}.");
        }

        return new CursorShape(image, hotspot);
    }

    /// <summary>Gets where the image lies on the screen when the pointer is at <paramref name="position"/>.</summary>
    public Rectangle GetBounds(Point position)
    {
        return new Rectangle(position.X - Hotspot.X, position.Y - Hotspot.Y, Width, Height);
    }
}
