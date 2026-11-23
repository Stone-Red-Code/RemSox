using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Shapes;

public sealed class Line() : Shape("Line")
{
    public Point EndPosition
    {
        get;
        set => SetProperty(nameof(EndPosition), ref field, value);
    }

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        yield return new LinePrimitive
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Position = Position,
            Color = Color,
            End = EndPosition,
        };
    }
}