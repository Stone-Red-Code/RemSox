using RemSox.Shared.UI.GUI.Rendering;

namespace RemSox.Kernel.UI.GUI.UIEelements.Shapes;

public sealed class Circle() : Shape("Circle")
{
    public int Radius
    {
        get;
        set => SetProperty(nameof(Radius), ref field, value);
    }

    public bool IsFilled
    {
        get;
        set => SetProperty(nameof(IsFilled), ref field, value);
    } = true;

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        yield return IsFilled
            ? new FilledCircle { WindowId = windowId, ElementId = PrimitiveId(0), Position = Position, Color = Resolve(Color), Radius = Radius }
            : new CircleOutline { WindowId = windowId, ElementId = PrimitiveId(0), Position = Position, Color = Resolve(Color), Radius = Radius };
    }
}