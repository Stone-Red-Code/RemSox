using System;
using System.Drawing;

namespace RemSox.UI.GUI.UIEelements;

public abstract class Control(string type) : UIElement(type)
{
    public Color BackgroundColor
    {
        get;
        set => SetProperty(nameof(BackgroundColor), ref field, value);
    } = Color.LightGray;

    public Size Size
    {
        get;
        set => SetProperty(nameof(Size), ref field, value);
    }
}
