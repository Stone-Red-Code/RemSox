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

    /// <summary>Gets or sets the text's colour: a fixed one, or a role of the theme (<see cref="ThemeRole.Text"/> by default).</summary>
    public ThemeColor Color
    {
        get;
        set => SetProperty(nameof(Color), ref field, value);
    } = ThemeRole.Text;

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

    public override Size Extent => new(Math.Min(MaxWidth, Content.Length * Utils.CanvasExtensions.GetCellWidth(FontSize)), FontSize);

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        yield return new TextPrimitive
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Position = Position,
            Color = Resolve(Color),
            Content = Content,
            FontSize = FontSize,
            MaxWidth = MaxWidth,
        };
    }
}
