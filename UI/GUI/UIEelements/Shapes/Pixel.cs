using RemSox.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.UI.GUI.UIEelements.Shapes;

public class Pixel() : Shape("Pixel")
{
    public override IEnumerable<RenderCommand> ToPrimitives(int windowId)
    {
        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Type = RenderCommandType.DrawPoint,
            Position = Position,
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = Color,
            }
        };
    }
}
