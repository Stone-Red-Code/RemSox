using RemSox.UI.GUI.Rendering;

namespace RemSox.UI.GUI.UIEelements.Shapes;

public class Circle() : Shape("Circle")
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

    public override IEnumerable<RenderCommand> ToPrimitives(int windowId)
    {
        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Type = IsFilled ? RenderCommandType.DrawFilledCircle : RenderCommandType.DrawCircle,
            Position = Position,
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = Color,
                ["Radius"] = Radius,
            }
        };
    }
}