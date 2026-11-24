using Cosmos.Kernel.System.Input;

using RemSox.Kernel.Utils;
using RemSox.Shared.UI;
using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

/// <summary>A column of a <see cref="ListView"/>: its title and how wide it is, in pixels.</summary>
public sealed record ListColumn(string Title, int Width);

/// <summary>
/// Rows of text in columns under a header, one of which can be selected: a process list, the
/// file associations. Clicking a row selects it and clicking it again quickly (or Enter)
/// activates it; the arrows, Page Up, Page Down, Home and End move the selection while the list
/// is focused, and the wheel scrolls. Clicking a column's title sorts by it, and again the
/// other way. The row under the pointer is washed lighter.
/// </summary>
/// <remarks>
/// The program gives the rows with <see cref="SetRows"/>, each with a key, as often as it likes
/// (a refresh): the selection follows the key (<see cref="ListRows"/>). Only the rows in view
/// are drawn, one text each, so a list of any length costs the same to show. Give it a
/// <see cref="ScrollBar"/> (made by the owner in the same window) to show and move which rows
/// are in view, as with a <see cref="TextArea"/>.
/// </remarks>
public sealed class ListView() : Control("ListView", ThemeRole.Field)
{
    private const int Padding = 2;
    private const int TextIndent = 6;
    private const long DoubleClickMilliseconds = 500;

    // Each row is a primitive of the element, which has 64: the frame, header, line,
    // selection and hover take 6.
    private const int MaxRows = 56;
    private const int FirstRowPrimitive = 6;

    private readonly ListRows list = new();
    private IReadOnlyList<ListColumn> columns = [];
    private ScrollBar? scrollBar;
    private int hoveredRow = -1;
    private int lastClickedRow = -1;
    private long lastClickTime;

    /// <summary>Raised with the selected row's key (null for none) when the user or <see cref="SetRows"/> changes it.</summary>
    public event Action<string?>? SelectionChanged;

    /// <summary>Raised with a row's key when it is double clicked, or Enter is pressed on it.</summary>
    public event Action<string>? RowActivated;

    /// <summary>Gets or sets the columns; a row's cells go into them in order.</summary>
    public IReadOnlyList<ListColumn> Columns
    {
        get => columns;
        set
        {
            columns = value;
            Changed();
        }
    }

    /// <summary>Gets the rows in the order shown.</summary>
    public IReadOnlyList<ListRow> Rows => list.Rows;

    /// <summary>Gets the selected row's key, or null.</summary>
    public string? SelectedKey => list.SelectedKey;

    /// <summary>Gets the first row in view.</summary>
    public int TopRow => list.TopRow;

    public int FontSize
    {
        get;
        set => SetProperty(nameof(FontSize), ref field, Math.Clamp(value, 8, 64));
    } = 16;

    /// <summary>Gets how many rows fit under the header.</summary>
    public int VisibleRows => Math.Clamp((Size.Height - (2 * Padding) - RowHeight) / RowHeight, 1, MaxRows);

    /// <summary>
    /// Gets or sets the scroll bar that shows and moves which rows are in view. The owner makes
    /// it in the list's window and places it; the list keeps its range and value.
    /// </summary>
    public ScrollBar? ScrollBar
    {
        get => scrollBar;
        set
        {
            if (scrollBar is not null)
            {
                scrollBar.Scrolled -= ScrollTo;
            }

            scrollBar = value;
            if (scrollBar is not null)
            {
                scrollBar.Scrolled += ScrollTo;
            }

            Changed();
        }
    }

    // Bumped on every change the other properties do not show (new rows, a selection, a
    // scroll), so the window draws the list again.
    private int Revision
    {
        get;
        set => SetProperty(nameof(Revision), ref field, value);
    }

    private int RowHeight => FontSize + 6;

    private int CellWidth => Math.Max(1, CanvasExtensions.GetCellWidth(FontSize));

    private Rectangle Inner => new(Position.X + Padding, Position.Y + Padding, Math.Max(0, Size.Width - (2 * Padding)), Math.Max(0, Size.Height - (2 * Padding)));

    private int RowsTop => Inner.Y + RowHeight;

    /// <summary>Replaces the rows, keeping the selected one selected (<see cref="ListRows.SetRows"/>).</summary>
    public void SetRows(IEnumerable<ListRow> rows)
    {
        bool selectionChanged = list.SetRows(rows);
        Changed();
        if (selectionChanged)
        {
            SelectionChanged?.Invoke(list.SelectedKey);
        }
    }

    /// <summary>Selects the row with <paramref name="key"/> (none when null or missing) and scrolls it into view.</summary>
    public void Select(string? key)
    {
        list.VisibleRows = VisibleRows;
        if (list.Select(key))
        {
            Changed();
            SelectionChanged?.Invoke(list.SelectedKey);
        }
    }

    /// <summary>Sorts by <paramref name="column"/> as a click on its title does: from the smallest up, or the other way when it is sorted by it already.</summary>
    public void SortBy(int column)
    {
        list.SortBy(column);
        Changed();
    }

    /// <summary>Scrolls so <paramref name="row"/> is the first in view (kept in range); the selection stays.</summary>
    public void ScrollTo(int row)
    {
        list.VisibleRows = VisibleRows;
        if (list.ScrollTo(row))
        {
            Changed();
        }
    }

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        if (Size.Width <= 0 || Size.Height <= 0)
        {
            yield break;
        }

        list.VisibleRows = VisibleRows;
        Rectangle inner = Inner;
        int rowHeight = RowHeight;
        int textOffset = (rowHeight - FontSize) / 2;
        List<int> widths = ColumnCharacters();

        yield return new FilledRoundedRectangle
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Position = Position,
            Color = Resolve(BackgroundColor),
            Size = Size,
            Radius = Theme.Radius,
        };

        yield return new RoundedRectangleBorder
        {
            WindowId = windowId,
            ElementId = PrimitiveId(1),
            Position = Position,
            Color = Theme[IsFocused ? ThemeRole.BorderFocus : ThemeRole.Border],
            Size = Size,
            Radius = Theme.Radius,
        };

        // The header: the titles, the sorted one marked, over a line.
        string[] titles = [.. columns.Select((column, i) => i == list.SortColumn ? column.Title + (list.SortDescending ? " v" : " ^") : column.Title)];
        yield return new TextPrimitive
        {
            WindowId = windowId,
            ElementId = PrimitiveId(2),
            Position = new Point(inner.X + TextIndent, inner.Y + textOffset),
            Color = Theme[ThemeRole.TextMuted],
            Content = ListRows.FormatRow(titles, widths),
            FontSize = FontSize,
            Clip = inner,
        };

        yield return new FilledRectangle
        {
            WindowId = windowId,
            ElementId = PrimitiveId(3),
            Position = new Point(inner.X, RowsTop - 1),
            Color = Theme[ThemeRole.Border],
            Size = new Size(inner.Width, 1),
        };

        Rectangle rowsArea = new(inner.X, RowsTop, inner.Width, Math.Max(0, inner.Bottom - RowsTop));
        int visible = list.VisibleRows;

        int selectedRow = list.SelectedIndex - list.TopRow;
        if (selectedRow >= 0 && selectedRow < visible)
        {
            yield return new FilledRoundedRectangle
            {
                WindowId = windowId,
                ElementId = PrimitiveId(4),
                Position = new Point(inner.X + 2, RowsTop + (selectedRow * rowHeight)),
                Color = Theme[ThemeRole.Selection],
                Size = new Size(Math.Max(0, inner.Width - 4), rowHeight),
                Radius = Theme.Radius,
                Clip = rowsArea,
            };
        }

        int hovered = hoveredRow - list.TopRow;
        if (IsHovered && hoveredRow != list.SelectedIndex && hovered >= 0 && hovered < visible && hoveredRow < list.Rows.Count)
        {
            yield return new FilledRoundedRectangle
            {
                WindowId = windowId,
                ElementId = PrimitiveId(5),
                Position = new Point(inner.X + 2, RowsTop + (hovered * rowHeight)),
                Color = Theme.GetInteractive(BackgroundColor, hovered: true, pressed: false),
                Size = new Size(Math.Max(0, inner.Width - 4), rowHeight),
                Radius = Theme.Radius,
                Clip = rowsArea,
            };
        }

        for (int row = 0; row < visible && list.TopRow + row < list.Rows.Count; row++)
        {
            yield return new TextPrimitive
            {
                WindowId = windowId,
                ElementId = PrimitiveId(FirstRowPrimitive + row),
                Position = new Point(inner.X + TextIndent, RowsTop + (row * rowHeight) + textOffset),
                Color = Theme[ThemeRole.Text],
                Content = ListRows.FormatRow(list.Rows[list.TopRow + row].Cells, widths),
                FontSize = FontSize,
                Clip = rowsArea,
            };
        }
    }

    public override void HandleKeyEvent(KeyEvent keyEvent)
    {
        list.VisibleRows = VisibleRows;
        switch (keyEvent.Key)
        {
            case Key.UpArrow:
                MoveSelection(-1);
                break;

            case Key.DownArrow:
                MoveSelection(1);
                break;

            case Key.PageUp:
                MoveSelection(-list.VisibleRows);
                break;

            case Key.PageDown:
                MoveSelection(list.VisibleRows);
                break;

            case Key.Home:
                MoveSelection(-list.Rows.Count);
                break;

            case Key.End:
                MoveSelection(list.Rows.Count);
                break;

            case Key.Enter or Key.NumEnter when list.SelectedKey is { } key:
                RowActivated?.Invoke(key);
                break;
        }
    }

    public override void HandleMouseEvent(MouseEvent mouseEvent)
    {
        list.VisibleRows = VisibleRows;
        switch (mouseEvent.Type)
        {
            case MouseEventType.ButtonDown when mouseEvent.Button == MouseButton.Left:
                if (mouseEvent.Y < RowsTop)
                {
                    SortAt(mouseEvent.X);
                    return;
                }

                int row = RowAt(mouseEvent.Y);
                if (row < 0)
                {
                    return;
                }

                long now = Environment.TickCount64;
                bool doubleClick = row == lastClickedRow && now - lastClickTime <= DoubleClickMilliseconds;
                lastClickedRow = doubleClick ? -1 : row;
                lastClickTime = now;

                if (list.Select(row))
                {
                    Changed();
                    SelectionChanged?.Invoke(list.SelectedKey);
                }

                if (doubleClick && list.SelectedKey is { } key)
                {
                    RowActivated?.Invoke(key);
                }
                break;

            case MouseEventType.Move:
                int under = mouseEvent.Y >= RowsTop ? RowAt(mouseEvent.Y) : -1;
                if (under != hoveredRow)
                {
                    hoveredRow = under;
                    Revision++;
                }
                break;

            case MouseEventType.Wheel:
                ScrollTo(list.TopRow + (mouseEvent.Delta * 3));
                break;
        }
    }

    /// <summary>Returns the row at <paramref name="y"/> in the window, or -1 where there is none.</summary>
    private int RowAt(int y)
    {
        int row = list.TopRow + ((y - RowsTop) / RowHeight);
        return y >= RowsTop && row < list.Rows.Count && row < list.TopRow + list.VisibleRows ? row : -1;
    }

    private void SortAt(int x)
    {
        int left = Inner.X + TextIndent;
        List<int> widths = ColumnCharacters();
        for (int i = 0; i < widths.Count; i++)
        {
            int right = left + (widths[i] * CellWidth);
            if (x < right || i == widths.Count - 1)
            {
                SortBy(i);
                return;
            }

            left = right;
        }
    }

    private void MoveSelection(int rowCount)
    {
        if (list.MoveSelection(rowCount))
        {
            Changed();
            SelectionChanged?.Invoke(list.SelectedKey);
        }
    }

    // The columns' widths in characters of the font.
    private List<int> ColumnCharacters()
    {
        int cell = CellWidth;
        return [.. columns.Select(column => Math.Max(1, column.Width / cell))];
    }

    /// <summary>Keeps the bar in step and has the list drawn again.</summary>
    private void Changed()
    {
        list.VisibleRows = VisibleRows;
        if (scrollBar is not null)
        {
            scrollBar.Maximum = list.Rows.Count;
            scrollBar.ViewportSize = list.VisibleRows;
            scrollBar.Value = list.TopRow;
        }

        Revision++;
    }
}
