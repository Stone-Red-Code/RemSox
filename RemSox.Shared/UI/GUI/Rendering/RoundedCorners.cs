namespace RemSox.Shared.UI.GUI.Rendering;

/// <summary>
/// How much of each pixel of a rounded corner a rounded rectangle covers, so a renderer that
/// draws pixel by pixel (the kernel's) can draw smooth corners: blend each corner pixel by its
/// coverage, and fill the rest as usual. Computed once per radius and kept.
/// </summary>
/// <remarks>
/// A table is the top left corner, <c>radius</c> by <c>radius</c> pixels, row by row: entry
/// <c>[y * radius + x]</c> is the coverage of the pixel at (x, y) from 0 (outside) to 255
/// (inside). The other corners are its mirror images. The corner's circle has its centre at
/// (radius, radius); a pixel's coverage is the share of 4x4 points in it that lie in the shape.
/// </remarks>
public static class RoundedCorners
{
    private const int Samples = 4;

    private static readonly Dictionary<int, byte[]> fills = [];
    private static readonly Dictionary<int, byte[]> outlines = [];

    /// <summary>Returns the coverage of a filled corner of <paramref name="radius"/> (at least 1).</summary>
    public static byte[] Fill(int radius)
    {
        if (!fills.TryGetValue(radius, out byte[]? table))
        {
            table = Make(radius, outline: false);
            fills[radius] = table;
        }

        return table;
    }

    /// <summary>Returns the coverage of a one-pixel outline's corner of <paramref name="radius"/> (at least 1).</summary>
    public static byte[] Outline(int radius)
    {
        if (!outlines.TryGetValue(radius, out byte[]? table))
        {
            table = Make(radius, outline: true);
            outlines[radius] = table;
        }

        return table;
    }

    /// <summary>Returns the radius a rectangle of <paramref name="width"/> by <paramref name="height"/> can have: at most half its shorter side.</summary>
    public static int Clamp(int radius, int width, int height)
    {
        return Math.Max(0, Math.Min(radius, Math.Min(width, height) / 2));
    }

    private static byte[] Make(int radius, bool outline)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(radius, 1);

        byte[] table = new byte[radius * radius];
        double outer = (double)radius * radius;
        double inner = (double)(radius - 1) * (radius - 1);
        for (int y = 0; y < radius; y++)
        {
            for (int x = 0; x < radius; x++)
            {
                int inside = 0;
                for (int sy = 0; sy < Samples; sy++)
                {
                    for (int sx = 0; sx < Samples; sx++)
                    {
                        double dx = radius - (x + ((sx + 0.5) / Samples));
                        double dy = radius - (y + ((sy + 0.5) / Samples));
                        double distance = (dx * dx) + (dy * dy);
                        if (distance <= outer && (!outline || distance >= inner))
                        {
                            inside++;
                        }
                    }
                }

                table[(y * radius) + x] = (byte)(inside * 255 / (Samples * Samples));
            }
        }

        return table;
    }
}
