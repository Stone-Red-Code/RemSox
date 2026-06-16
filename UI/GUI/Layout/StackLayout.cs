using RemSox.UI.GUI.UIEelements;
using RemSox.UI.GUI.Windows;

using System.Drawing;

namespace RemSox.UI.GUI.Layout;

public enum StackOrientation
{
    Vertical,
    Horizontal,
}

public sealed class StackLayout
{
    private readonly Window window;
    private int nextX;
    private int nextY;
    private readonly int spacing;
    private readonly StackOrientation orientation;

    public int? UniformWidth { get; set; }
    public int? UniformHeight { get; set; }

    public StackLayout(Window window, int x, int y, int spacing = 0, StackOrientation orientation = StackOrientation.Vertical)
    {
        this.window = window;
        nextX = x;
        nextY = y;
        this.spacing = spacing;
        this.orientation = orientation;
    }

    public T Add<T>(Action<T>? options = null) where T : UIElement, new()
    {
        bool wasAutoFlush = window.AutoFlush;
        window.AutoFlush = false;

        T element = window.CreateUIElement<T>(options);
        PositionElement(element);

        if (wasAutoFlush)
        {
            window.AutoFlush = true;
            window.Flush();
        }
        else
        {
            window.AutoFlush = false;
        }

        return element;
    }

    private void PositionElement(UIElement element)
    {
        element.Position = new Point(nextX, nextY);

        if (element is Control control)
        {
            if (UniformWidth.HasValue)
            {
                control.Size = new Size(UniformWidth.Value, control.Size.Height);
            }
            if (UniformHeight.HasValue)
            {
                control.Size = new Size(control.Size.Width, UniformHeight.Value);
            }
        }

        if (orientation == StackOrientation.Vertical)
        {
            int h = element is Control c ? c.Size.Height : 0;
            nextY += h + spacing;
        }
        else
        {
            int w = element is Control c ? c.Size.Width : 0;
            nextX += w + spacing;
        }
    }
}
