using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

using System.Drawing;
using System.Reflection;

namespace RemSox.Kernel.Utils;

public static class CanvasExtensions
{
    // The UI lays text out in fixed cells with the proportions of the 8x16 PSF font: a cell is
    // half as wide as it is high. Controls, the terminal and remote viewers all rely on that.
    private const int CellWidthUnits = 8;
    private const int CellHeightUnits = 16;

    private static readonly Lazy<TrueTypeFont?> uiFont = new(LoadUiFont);
    private static readonly Dictionary<int, int> sizeForCellHeight = [];

    /// <summary>Width of one character cell for text <paramref name="cellHeight"/> pixels high.</summary>
    public static int GetCellWidth(int cellHeight)
    {
        return CellWidthUnits * cellHeight / CellHeightUnits;
    }

    /// <summary>
    /// Draws <paramref name="text"/> one character per fixed cell, <paramref name="cellHeight"/>
    /// pixels high, with the anti-aliased TrueType UI font (or the PSF font if it failed to load).
    /// Characters that would end past <paramref name="maxWidth"/> are not drawn.
    /// </summary>
    public static void DrawCellText(this Canvas canvas, string text, Color color, int x, int y, int cellHeight, int maxWidth = int.MaxValue)
    {
        int cellWidth = GetCellWidth(cellHeight);
        if (cellWidth <= 0)
        {
            return;
        }

        int endX = maxWidth == int.MaxValue ? int.MaxValue : x + maxWidth;
        TrueTypeFont? font = uiFont.Value;

        if (font is null)
        {
            DrawPsfText(canvas, text, color, x, y, cellWidth, cellHeight, endX);
            return;
        }

        int sizePx = GetFittingSize(font, cellWidth, cellHeight);
        int glyphTop = y + ((cellHeight - font.GetLineHeight(sizePx)) / 2);

        foreach (char c in text)
        {
            if (x + cellWidth > endX)
            {
                break;
            }

            if (!char.IsWhiteSpace(c))
            {
                int glyphLeft = x + ((cellWidth - font.GetAdvance(c, sizePx)) / 2);
                canvas.DrawString(c.ToString(), font, sizePx, color, glyphLeft, glyphTop);
            }

            x += cellWidth;
        }
    }

    /// <summary>
    /// The largest pixel size at which a glyph fits a cell: at most the cell's height, and narrow
    /// enough that the font's advance is no wider than the cell.
    /// </summary>
    private static int GetFittingSize(TrueTypeFont font, int cellWidth, int cellHeight)
    {
        if (sizeForCellHeight.TryGetValue(cellHeight, out int cached))
        {
            return cached;
        }

        int size = cellHeight;
        while (size > 1 && (font.GetAdvance('M', size) > cellWidth || font.GetLineHeight(size) > cellHeight))
        {
            size--;
        }

        sizeForCellHeight[cellHeight] = size;
        return size;
    }

    private static TrueTypeFont? LoadUiFont()
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DejaVuSansMono.ttf");
            if (stream is not null)
            {
                return new TrueTypeFont(stream);
            }

            Console.WriteLine("UI font resource missing, using the PSF font.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not load the UI font, using the PSF font: " + ex.Message);
        }

        return null;
    }

    /// <summary>The PSF fallback: each bitmap glyph scaled to fill its cell.</summary>
    private static void DrawPsfText(Canvas canvas, string text, Color color, int x, int y, int cellWidth, int cellHeight, int endX)
    {
        Font font = PCScreenFont.DefaultFont;
        byte height = font.Height;
        byte width = font.Width;
        byte[] data = font.Data;
        int bytesPerRow = (width + 7) / 8;

        foreach (char c in text)
        {
            if (x + cellWidth > endX)
            {
                break;
            }

            int p = height * bytesPerRow * (byte)c;

            for (int cy = 0; cy < height; cy++)
            {
                int startY = cy * cellHeight / height;
                int stopY = (((cy + 1) * cellHeight) + height - 1) / height;

                for (int cx = 0; cx < width; cx++)
                {
                    if (!font.ConvertByteToBitAddress(data[p + (cy * bytesPerRow) + (cx / 8)], (cx % 8) + 1))
                    {
                        continue;
                    }

                    int startX = cx * cellWidth / width;
                    int stopX = (((cx + 1) * cellWidth) + width - 1) / width;

                    canvas.DrawFilledRectangle(color, x + startX, y + startY, stopX - startX, stopY - startY);
                }
            }

            x += cellWidth;
        }
    }
}
