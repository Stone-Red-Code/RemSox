using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements;

public abstract class Shape(string type) : UIElement(type)
{
    public Color Color
    {
        get;
        set => SetProperty(nameof(Color), ref field, value);
    }
}