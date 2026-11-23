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

    public bool IsFilled
    {
        get;
        set => SetProperty(nameof(IsFilled), ref field, value);
    } = true;

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        yield return IsFilled
            ? new FilledRectangle { WindowId = windowId, ElementId = PrimitiveId(0), Position = Position, Color = Color, Size = Size }
            : new RectangleBorder { WindowId = windowId, ElementId = PrimitiveId(0), Position = Position, Color = Color, Size = Size };
    }
}
