using System.Drawing;

namespace RemSox.UI.GUI.UIEelements.Shapes;

public class Rectangle() : Shape("Rectangle")
{
    public Size Size
    {
        get;
        set => SetProperty(nameof(Size), ref field, value);
    }

    public bool IsFilled
    {
        get;
        set => SetProperty(nameof(IsFilled), ref field, value);
    } = true;
}
