using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Shapes;

public sealed class Text() : UIElement("Text")
{
    public string Content
    {
        get;
        set => SetProperty(nameof(Content), ref field, value);
    } = string.Empty;

    public Color Color
    {
        get;
        set => SetProperty(nameof(Color), ref field, value);
    } = Color.White;

    public int FontSize
    {
        get;
        set => SetProperty(nameof(FontSize), ref field, value);
    } = 12;

    public int MaxWidth
    {
        get;
        set => SetProperty(nameof(MaxWidth), ref field, value);
    } = int.MaxValue;

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        yield return new TextPrimitive
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Position = Position,
            Color = Color,
            Content = Content,
            FontSize = FontSize,
            MaxWidth = MaxWidth,
        };
    }
}
