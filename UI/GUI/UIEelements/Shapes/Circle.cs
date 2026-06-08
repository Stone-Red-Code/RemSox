using System.Drawing;

namespace RemSox.UI.GUI.UIEelements.Shapes;

public class Circle() : Shape("Circle")
{
    public int Radius
    {
        get;
        set => SetProperty(nameof(Radius), ref field, value);
    }
}