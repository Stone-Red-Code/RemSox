using RemSox.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.UI.GUI.UIEelements.Shapes;

public class Line() : Shape("Line")
{
    public Point EndPosition
    {
        get;
        set => SetProperty(nameof(EndPosition), ref field, value);
    }

    public override IEnumerable<RenderCommand> ToPrimitives(int windowId)
    {
        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Type = RenderCommandType.DrawLine,
            Position = Position,
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = Color,
                ["EndPosition"] = EndPosition,
            }
        };
    }
}