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
}
