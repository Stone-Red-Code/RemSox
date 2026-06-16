using RemSox.UI.GUI.Windows;

namespace RemSox.UI.GUI.Layout;

public static class LayoutExtensions
{
    public static StackLayout CreateStackLayout(this Window window, int x, int y, int spacing = 0, StackOrientation orientation = StackOrientation.Vertical)
    {
        return new StackLayout(window, x, y, spacing, orientation);
    }

    public static GridLayout CreateGridLayout(this Window window, int x, int y, int[] colWidths, int[] rowHeights, int spacing = 0)
    {
        return new GridLayout(window, x, y, colWidths, rowHeights, spacing);
    }
}
