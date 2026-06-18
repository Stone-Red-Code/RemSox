using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

using System.Drawing;

namespace RemSox.Utils;

public static class CanvasExtensions
{
    public static void DrawStringScale(this Canvas canvas, string str, Font font, Color color, int x, int y, int scale, int maxWidth = int.MaxValue)
    {
        int len = str.Length;
        byte width = font.Width;

        int targetWidth = width * scale;

        for (int i = 0; i < len; i++)
        {
            if (x + targetWidth > maxWidth)
            {
                break;
            }

            canvas.DrawCharScale(str[i], font, color, x, y, scale);
            x += targetWidth;
        }
    }

    public static void DrawCharScale(this Canvas canvas, char c, Font font, Color color, int x, int y, int scale)
    {
        byte height = font.Height;
        byte width = font.Width;
        byte[] data = font.Data;
        int bytesPerRow = (width + 7) / 8;
        int p = height * bytesPerRow * (byte)c;

        for (int cy = 0; cy < height; cy++)
        {
            for (byte cx = 0; cx < width; cx++)
            {
                byte byteValue = data[p + (cy * bytesPerRow) + (cx / 8)];

                if (font.ConvertByteToBitAddress(byteValue, (cx % 8) + 1))
                {
                    if (scale == 1)
                    {
                        canvas.DrawPoint(color, (ushort)(x + cx), (ushort)(y + cy));
                    }
                    else
                    {
                        for (int sy = 0; sy < scale; sy++)
                        {
                            for (int sx = 0; sx < scale; sx++)
                            {
                                canvas.DrawPoint(
                                    color,
                                    (ushort)(x + (cx * scale) + sx),
                                    (ushort)(y + (cy * scale) + sy));
                            }
                        }
                    }
                }
            }
        }
    }

    public static void DrawStringHeight(this Canvas canvas, string str, Font font, Color color, int x, int y, int targetHeight, int maxWidth = int.MaxValue)
    {
        byte width = font.Width;
        byte height = font.Height;

        int targetWidth = width * targetHeight / height;
        int endX = maxWidth == int.MaxValue ? int.MaxValue : x + maxWidth;

        for (int i = 0; i < str.Length; i++)
        {
            if (x + targetWidth > endX)
            {
                break;
            }

            canvas.DrawCharHeight(str[i], font, color, x, y, targetHeight);
            x += targetWidth;
        }
    }

    public static void DrawCharHeight(this Canvas canvas, char c, Font font, Color color, int x, int y, int targetHeight)
    {
        byte height = font.Height;
        byte width = font.Width;
        byte[] data = font.Data;

        int targetWidth = width * targetHeight / height;
        int bytesPerRow = (width + 7) / 8;
        int p = height * bytesPerRow * (byte)c;

        for (int cy = 0; cy < height; cy++)
        {
            int startY = cy * targetHeight / height;
            int endY = (((cy + 1) * targetHeight) + height - 1) / height;

            for (int cx = 0; cx < width; cx++)
            {
                byte byteValue = data[p + (cy * bytesPerRow) + (cx / 8)];

                if (font.ConvertByteToBitAddress(byteValue, (cx % 8) + 1))
                {
                    int startX = cx * targetWidth / width;
                    int endX = (((cx + 1) * targetWidth) + width - 1) / width;

                    for (int sy = startY; sy < endY; sy++)
                    {
                        for (int sx = startX; sx < endX; sx++)
                        {
                            canvas.DrawPoint(color, (ushort)(x + sx), (ushort)(y + sy));
                        }
                    }
                }
            }
        }
    }
}
