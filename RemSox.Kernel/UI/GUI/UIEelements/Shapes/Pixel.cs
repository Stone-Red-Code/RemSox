using RemSox.Shared.UI.GUI.Rendering;

namespace RemSox.Kernel.UI.GUI.UIEelements.Shapes;

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
