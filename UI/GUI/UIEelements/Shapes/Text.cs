using RemSox.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.UI.GUI.UIEelements.Shapes;

public class Text() : UIElement("Text")
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

    public override IEnumerable<RenderCommand> ToPrimitives(int windowId)
    {
        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Type = RenderCommandType.DrawText,
            Position = Position,
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = Color,
                ["Content"] = Content,
                ["FontSize"] = FontSize,
                ["MaxWidth"] = MaxWidth,
            }
        };
    }
}
