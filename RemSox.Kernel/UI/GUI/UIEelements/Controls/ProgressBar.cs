using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

public sealed class ProgressBar() : Control("ProgressBar")
{
    public int Value
    {
        get;
        set => SetProperty(nameof(Value), ref field, value);
    }

    public Color FillColor
    {
        get;
        set => SetProperty(nameof(FillColor), ref field, value);
    } = Color.Green;

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        // Background
        yield return new FilledRectangle
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Position = Position,
            Color = BackgroundColor,
            Size = Size,
        };

        // Fill
        int fillWidth = Size.Width * Math.Clamp(Value, 0, 100) / 100;

        if (fillWidth > 0)
        {
            yield return new FilledRectangle
            {
                WindowId = windowId,
                ElementId = PrimitiveId(1),
                Position = Position,
                Color = FillColor,
                Size = new Size(fillWidth, Size.Height),
            };
        }

        // Border
        yield return new RectangleBorder
        {
            WindowId = windowId,
            ElementId = PrimitiveId(2),
            Position = Position,
            Color = Color.DarkGray,
            Size = Size,
        };
    }
}
