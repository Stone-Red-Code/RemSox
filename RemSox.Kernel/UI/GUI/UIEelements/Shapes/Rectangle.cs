using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Shapes;

public sealed class Rectangle() : Shape("Rectangle")
{
    public Size Size
    {
        get;
        set => SetProperty(nameof(Size), ref field, value);
    }

    public override Size Extent => Size;

    public bool IsFilled
    {
        get;
        set => SetProperty(nameof(IsFilled), ref field, value);
    } = true;

    /// <summary>Gets or sets the corners' radius in pixels: 0 (the default) for square corners.</summary>
    public int CornerRadius
    {
        get;
        set => SetProperty(nameof(CornerRadius), ref field, value);
    }

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        Color color = Resolve(Color);
        if (CornerRadius > 0)
        {
            yield return IsFilled
                ? new FilledRoundedRectangle { WindowId = windowId, ElementId = PrimitiveId(0), Position = Position, Color = color, Size = Size, Radius = CornerRadius }
                : new RoundedRectangleBorder { WindowId = windowId, ElementId = PrimitiveId(0), Position = Position, Color = color, Size = Size, Radius = CornerRadius };
            yield break;
        }

        yield return IsFilled
            ? new FilledRectangle { WindowId = windowId, ElementId = PrimitiveId(0), Position = Position, Color = color, Size = Size }
            : new RectangleBorder { WindowId = windowId, ElementId = PrimitiveId(0), Position = Position, Color = color, Size = Size };
    }
}
