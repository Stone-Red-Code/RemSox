using RemSox.Shared.UI.GUI.Rendering;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

public class Panel() : Control("Panel")
{
    public override IEnumerable<RenderCommand> ToPrimitives(int windowId)
    {
        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Type = RenderCommandType.DrawFilledRect,
            Position = Position,
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = BackgroundColor,
                ["Size"] = Size,
            }
        };
    }
}
