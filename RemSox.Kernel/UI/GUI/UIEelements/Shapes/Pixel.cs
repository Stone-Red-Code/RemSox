using RemSox.Shared.UI.GUI.Rendering;

namespace RemSox.Kernel.UI.GUI.UIEelements.Shapes;

public sealed class Pixel() : Shape("Pixel")
{
    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        yield return new PointPrimitive
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Position = Position,
            Color = Resolve(Color),
        };
    }
}
