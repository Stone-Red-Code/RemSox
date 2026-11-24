using System.Globalization;
using System.Text;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

/// <summary>One row of a <see cref="ListView"/>: a key that stays the same while the row's cells change, and the cells.</summary>
/// <param name="Key">What the row is (a process id, a setting's name): the selection follows it when the rows are replaced.</param>
/// <param name="Cells">The text of each column.</param>
public sealed record ListRow(string Key, IReadOnlyList<string> Cells);

/// <summary>
/// What a <see cref="ListView"/> shows, apart from drawing it: the rows in their order, which
/// one is selected, which are in view, and how they are sorted. Kept apart from the control
/// so it can be tested without a window.
/// </summary>
/// <remarks>
/// The selection is a row's key, not its place: replacing the rows (a refresh) keeps the same
/// row selected wherever sorting puts it. When that row is gone, the one now at its place is
/// selected, as Windows' lists do after a delete, so a list worked through row by row keeps
/// its place.
/// </remarks>
public sealed class ListRows
{
    private readonly List<ListRow> unsorted = [];
    private List<ListRow> rows = [];

    /// <summary>Gets the rows in the order shown.</summary>
    public IReadOnlyList<ListRow> Rows => rows;

    /// <summary>Gets the selected row's place in <see cref="Rows"/>, or -1 when none is selected.</summary>
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>Gets the selected row's key, or null when none is selected.</summary>
    public string? SelectedKey => SelectedIndex >= 0 ? rows[SelectedIndex].Key : null;

    /// <summary>Gets the first row in view.</summary>
    public int TopRow { get; private set; }

    /// <summary>Gets or sets how many rows fit in view; the control sets it from its height.</summary>
    public int VisibleRows
    {
        get;
        set
        {
            field = Math.Max(1, value);
            ScrollTo(TopRow);
        }
    } = 1;

    /// <summary>Gets the column the rows are sorted by, or -1 for the order they were given in.</summary>
    public int SortColumn { get; private set; } = -1;

    /// <summary>Gets whether the rows are sorted from the largest down.</summary>
    public bool SortDescending { get; private set; }

    /// <summary>
    /// Replaces the rows, sorting them as before and keeping the selected key selected (or, if
    /// its row is gone, the row now at its place).
    /// </summary>
    /// <returns>Whether the selected key changed.</returns>
    public bool SetRows(IEnumerable<ListRow> newRows)
    {
        string? key = SelectedKey;
        int index = SelectedIndex;

        unsorted.Clear();
        unsorted.AddRange(newRows);
        Sort();

        return Reselect(key, index);
    }

    /// <summary>
    /// Sorts by <paramref name="column"/>: from the smallest up the first time, the other way
    /// when it is already sorted by it. Cells that are numbers on both sides compare as numbers.
    /// </summary>
    public void SortBy(int column)
    {
        if (column < 0)
        {
            return;
        }

        SortDescending = column == SortColumn && !SortDescending;
        SortColumn = column;

        string? key = SelectedKey;
        Sort();
        _ = Reselect(key, SelectedIndex);
    }

    /// <summary>Selects the row at <paramref name="index"/> (kept in range; -1 for none) and scrolls it into view.</summary>
    /// <returns>Whether the selection changed.</returns>
    public bool Select(int index)
    {
        int before = SelectedIndex;
        SelectedIndex = rows.Count == 0 || index < 0 ? -1 : Math.Min(index, rows.Count - 1);
        if (SelectedIndex >= 0)
        {
            ScrollIntoView(SelectedIndex);
        }

        return SelectedIndex != before;
    }

    /// <summary>Selects the row with <paramref name="key"/>, or none when there is no such row.</summary>
    /// <returns>Whether the selection changed.</returns>
    public bool Select(string? key)
    {
        return Select(IndexOf(key));
    }

    /// <summary>
    /// Moves the selection <paramref name="rowCount"/> rows down (up when negative), stopping at
    /// the ends; with none selected, a move down selects the first row and up the last.
    /// </summary>
    /// <returns>Whether the selection changed.</returns>
    public bool MoveSelection(int rowCount)
    {
        if (rows.Count == 0)
        {
            return false;
        }

        if (SelectedIndex < 0)
        {
            return Select(rowCount > 0 ? 0 : rows.Count - 1);
        }

        return Select(Math.Clamp(SelectedIndex + rowCount, 0, rows.Count - 1));
    }

    /// <summary>Scrolls so <paramref name="row"/> is the first in view, kept so the view is never past the last row.</summary>
    /// <returns>Whether the view moved.</returns>
    public bool ScrollTo(int row)
    {
        int before = TopRow;
        TopRow = Math.Clamp(row, 0, Math.Max(0, rows.Count - VisibleRows));
        return TopRow != before;
    }

    /// <summary>Returns the place of the row with <paramref name="key"/>, or -1.</summary>
    public int IndexOf(string? key)
    {
        if (key is null)
        {
            return -1;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Key == key)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Lays <paramref name="cells"/> out as one line of fixed-width characters, each in its
    /// column of <paramref name="widths"/> characters: a cell too long is cut, leaving one
    /// space before the next column. The font has cells of one width, so this lines the
    /// columns up as well as drawing each cell apart would, with one text per row.
    /// </summary>
    public static string FormatRow(IReadOnlyList<string> cells, IReadOnlyList<int> widths)
    {
        StringBuilder line = new();
        for (int i = 0; i < widths.Count; i++)
        {
            string cell = i < cells.Count ? cells[i] : string.Empty;
            int width = Math.Max(0, widths[i]);
            bool last = i == widths.Count - 1;
            int room = last ? width : Math.Max(0, width - 1);

            string shown = cell.Length > room ? cell[..room] : cell;
            _ = line.Append(shown);
            if (!last)
            {
                _ = line.Append(' ', width - shown.Length);
            }
        }

        return line.ToString().TrimEnd();
    }

    private void Sort()
    {
        rows = [.. unsorted];
        if (SortColumn < 0)
        {
            return;
        }

        int column = SortColumn;
        int sign = SortDescending ? -1 : 1;

        // A stable sort, so rows with equal cells keep the order they were given in.
        List<(ListRow Row, int Order)> ordered = [.. rows.Select((row, i) => (row, i))];
        ordered.Sort((a, b) =>
        {
            int compared = Compare(Cell(a.Row, column), Cell(b.Row, column));
            return compared != 0 ? sign * compared : a.Order.CompareTo(b.Order);
        });
        rows = [.. ordered.Select(entry => entry.Row)];
    }

    private bool Reselect(string? key, int index)
    {
        int found = IndexOf(key);
        if (key is not null && found < 0)
        {
            // Its row is gone: the one now at its place, or the last.
            found = rows.Count == 0 ? -1 : Math.Min(index, rows.Count - 1);
        }

        SelectedIndex = found;
        ScrollTo(TopRow);
        return SelectedKey != key;
    }

    private void ScrollIntoView(int row)
    {
        if (row < TopRow)
        {
            TopRow = row;
        }
        else if (row >= TopRow + VisibleRows)
        {
            TopRow = row - VisibleRows + 1;
        }
    }

    private static string Cell(ListRow row, int column)
    {
        return column < row.Cells.Count ? row.Cells[column] : string.Empty;
    }

    private static int Compare(string a, string b)
    {
        if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
            && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
        {
            return x.CompareTo(y);
        }

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
