using System.Drawing;

namespace RemSox.UI.GUI.UIEelements.Controls;

public class Button() : Control("Button")
{
    public string Text
    {
        get;
        set => SetProperty(nameof(Text), ref field, value);
    } = string.Empty;

    public Color TextColor
    {
        get;
        set => SetProperty(nameof(TextColor), ref field, value);
    } = Color.Black;
}