using RemSox.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.UI.GUI.UIEelements.Shapes;

public class Rectangle() : Shape("Rectangle")
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

    public override IEnumerable<RenderCommand> ToPrimitives(int windowId)
    {
        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Type = IsFilled ? RenderCommandType.DrawFilledRect : RenderCommandType.DrawRectBorder,
            Position = Position,
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = Color,
                ["Size"] = Size,
            }
        };
    }
}
