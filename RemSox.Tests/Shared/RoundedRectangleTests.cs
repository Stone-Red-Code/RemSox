using System.Drawing;

using RemSox.Shared.UI.GUI.Rendering;

namespace RemSox.Tests.Shared;

/// <summary>Rounded rectangles, their corners' coverage, and colours with alpha on the wire.</summary>
public sealed class RoundedRectangleTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void AFilledCornerIsSolidInsideAndEmptyAtItsTip(int radius)
    {
        byte[] corner = RoundedCorners.Fill(radius);

        Assert.Equal(radius * radius, corner.Length);
        Assert.Equal(255, corner[^1]); // next to the inside of the rectangle
        Assert.True(corner[0] < 128, $"the outermost pixel is {corner[0]} covered");

        // The circle is the same both ways, and coverage only grows towards the inside.
        for (int y = 0; y < radius; y++)
        {
            for (int x = 0; x < radius; x++)
            {
                Assert.Equal(corner[(y * radius) + x], corner[(x * radius) + y]);
                if (x + 1 < radius)
                {
                    Assert.True(corner[(y * radius) + x] <= corner[(y * radius) + x + 1]);
                }
            }
        }
    }

    [Fact]
    public void AnOutlinesCornerMeetsItsStraightSides()
    {
        const int radius = 8;
        byte[] corner = RoundedCorners.Outline(radius);

        // The pixels where the corner joins the top and left sides are the line itself; the
        // corner's inside is empty.
        Assert.True(corner[radius - 1] > 200, $"top join {corner[radius - 1]}");
        Assert.True(corner[(radius - 1) * radius] > 200, $"left join {corner[(radius - 1) * radius]}");
        Assert.Equal(0, corner[^1]);
        Assert.Same(corner, RoundedCorners.Outline(radius));
    }

    [Theory]
    [InlineData(8, 40, 28, 8)]
    [InlineData(20, 40, 28, 14)]
    [InlineData(5, 3, 40, 1)]
    [InlineData(-2, 40, 40, 0)]
    public void TheRadiusFitsTheRectangle(int radius, int width, int height, int expected)
    {
        Assert.Equal(expected, RoundedCorners.Clamp(radius, width, height));
    }

    [Fact]
    public void RoundedRectanglesAndAlphaGoOverTheWireUnchanged()
    {
        Color translucent = Color.FromArgb(128, 10, 20, 30);
        FilledRoundedRectangle filled = new() { WindowId = 1, ElementId = 2, Position = new Point(3, 4), Color = translucent, Size = new Size(50, 20), Radius = 6 };
        RoundedRectangleBorder border = new() { WindowId = 1, ElementId = 3, Position = new Point(3, 4), Color = Color.Red, Size = new Size(50, 20), Radius = 6 };

        FilledRoundedRectangle readFilled = Assert.IsType<FilledRoundedRectangle>(RenderCommand.FromBytes(filled.ToBytes()));
        RoundedRectangleBorder readBorder = Assert.IsType<RoundedRectangleBorder>(RenderCommand.FromBytes(border.ToBytes()));

        Assert.Equal(translucent.ToArgb(), readFilled.Color.ToArgb());
        Assert.Equal(new Size(50, 20), readFilled.Size);
        Assert.Equal(6, readFilled.Radius);
        Assert.Equal(Color.Red.ToArgb(), readBorder.Color.ToArgb());
        Assert.Equal(6, readBorder.Radius);
    }
}
