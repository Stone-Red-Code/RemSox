using System.Drawing;

namespace RemSox.UI.GUI.UIEelements.Shapes;

public class Line() : Shape("Line")
{
    public Point EndPosition
    {
        get;
        set => SetProperty(nameof(EndPosition), ref field, value);
    }
}