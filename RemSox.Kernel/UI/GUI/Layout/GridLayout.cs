using RemSox.Kernel.UI.GUI.UIEelements;
using RemSox.Kernel.UI.GUI.Windows;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.Layout;

public sealed class GridLayout
{
    private readonly Window window;
    private readonly int originX;
    private readonly int originY;
    private readonly int[] colWidths;
    private readonly int[] rowHeights;
    private readonly int spacing;

    public GridLayout(Window window, int x, int y, int[] colWidths, int[] rowHeights, int spacing = 0)
    {
        this.window = window;
        originX = x;
        originY = y;
        this.colWidths = colWidths;
        this.rowHeights = rowHeights;
        this.spacing = spacing;
    }

    public T Add<T>(int col, int row, Action<T>? options = null) where T : UIElement, new()
    {
        bool wasAutoFlush = window.AutoFlush;
        window.AutoFlush = false;

        T element = window.CreateUIElement<T>(options);
        PositionElement(element, col, row);

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

    private void PositionElement(UIElement element, int col, int row)
    {
        int cellX = CellOrigin(colWidths, col);
        int cellY = CellOrigin(rowHeights, row);

        element.Position = new Point(originX + cellX, originY + cellY);

        if (element is Control control)
        {
            control.Size = new Size(colWidths[col], rowHeights[row]);
        }
    }

    private int CellOrigin(int[] sizes, int index)
    {
        int offset = 0;
        for (int i = 0; i < index; i++)
        {
            offset += sizes[i] + spacing;
        }
        return offset;
    }
}
