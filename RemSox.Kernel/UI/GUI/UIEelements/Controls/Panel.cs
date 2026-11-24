using RemSox.Shared.UI.GUI.Rendering;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

public sealed class Panel() : Control("Panel", ThemeRole.Surface)
{
    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        yield return new FilledRectangle
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Position = Position,
            Color = Resolve(BackgroundColor),
            Size = Size,
        };
    }
}
