using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

public sealed class ProgressBar() : Control("ProgressBar", ThemeRole.Field)
{
    public int Value
    {
        get;
        set => SetProperty(nameof(Value), ref field, value);
    }

    public ThemeColor FillColor
    {
        get;
        set => SetProperty(nameof(FillColor), ref field, value);
    } = ThemeRole.Accent;

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        // Background
        yield return new FilledRoundedRectangle
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Position = Position,
            Color = Resolve(BackgroundColor),
            Size = Size,
            Radius = Theme.Radius,
        };

        // Fill
        int fillWidth = Size.Width * Math.Clamp(Value, 0, 100) / 100;

        if (fillWidth > 0)
        {
            yield return new FilledRoundedRectangle
            {
                WindowId = windowId,
                ElementId = PrimitiveId(1),
                Position = Position,
                Color = Resolve(FillColor),
                Size = new Size(fillWidth, Size.Height),
                Radius = Theme.Radius,
            };
        }

        // Border
        yield return new RoundedRectangleBorder
        {
            WindowId = windowId,
            ElementId = PrimitiveId(2),
            Position = Position,
            Color = Theme[ThemeRole.Border],
            Size = Size,
            Radius = Theme.Radius,
        };
    }
}
