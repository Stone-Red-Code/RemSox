using RemSox.Kernel.UI.GUI.UIEelements.Controls;

namespace RemSox.Tests.Kernel;

/// <summary>A list control's rows: the selection following its key, scrolling, sorting and the columns' layout.</summary>
public sealed class ListRowsTests
{
    private static ListRow Row(string key, params string[] cells)
    {
        return new ListRow(key, cells);
    }

    private static List<ListRow> Numbered(int count)
    {
        return [.. Enumerable.Range(0, count).Select(i => Row(i.ToString(), "row " + i))];
    }

    [Fact]
    public void TheSelectionFollowsItsKeyWhenTheRowsAreReplaced()
    {
        ListRows list = new();
        _ = list.SetRows([Row("a", "alpha"), Row("b", "beta"), Row("c", "gamma")]);
        Assert.True(list.Select("b"));

        // A refresh that puts b elsewhere keeps it selected, and says nothing changed.
        Assert.False(list.SetRows([Row("x", "new"), Row("a", "alpha"), Row("c", "gamma"), Row("b", "beta")]));
        Assert.Equal("b", list.SelectedKey);
        Assert.Equal(3, list.SelectedIndex);
    }

    [Fact]
    public void WhenTheSelectedRowGoesTheOneNowAtItsPlaceIsSelected()
    {
        ListRows list = new();
        _ = list.SetRows([Row("a"), Row("b"), Row("c")]);
        _ = list.Select("b");

        Assert.True(list.SetRows([Row("a"), Row("c")]));
        Assert.Equal("c", list.SelectedKey);

        // The last row gone: the new last one.
        Assert.True(list.SetRows([Row("a")]));
        Assert.Equal("a", list.SelectedKey);

        Assert.True(list.SetRows([]));
        Assert.Null(list.SelectedKey);
        Assert.Equal(-1, list.SelectedIndex);
    }

    [Fact]
    public void MovingTheSelectionStopsAtTheEndsAndScrollsItIntoView()
    {
        ListRows list = new() { VisibleRows = 4 };
        _ = list.SetRows(Numbered(10));

        // Nothing selected: down selects the first row, up the last.
        Assert.True(list.MoveSelection(1));
        Assert.Equal("0", list.SelectedKey);
        _ = list.Select(-1);
        Assert.True(list.MoveSelection(-1));
        Assert.Equal("9", list.SelectedKey);
        Assert.Equal(6, list.TopRow);

        Assert.False(list.MoveSelection(5));
        Assert.True(list.MoveSelection(-100));
        Assert.Equal("0", list.SelectedKey);
        Assert.Equal(0, list.TopRow);

        // Down past the view scrolls just enough to show it at the bottom.
        _ = list.MoveSelection(5);
        Assert.Equal(5, list.SelectedIndex);
        Assert.Equal(2, list.TopRow);
    }

    [Fact]
    public void TheViewNeverScrollsPastTheLastRow()
    {
        ListRows list = new() { VisibleRows = 4 };
        _ = list.SetRows(Numbered(10));

        Assert.True(list.ScrollTo(100));
        Assert.Equal(6, list.TopRow);
        Assert.True(list.ScrollTo(-3));
        Assert.Equal(0, list.TopRow);

        // Fewer rows than fit: nothing to scroll, and a shrinking list pulls the view back.
        _ = list.ScrollTo(6);
        _ = list.SetRows(Numbered(5));
        Assert.Equal(1, list.TopRow);
        _ = list.SetRows(Numbered(3));
        Assert.Equal(0, list.TopRow);
        Assert.False(list.ScrollTo(2));
    }

    [Fact]
    public void SortingComparesNumbersAsNumbersAndKeepsTheSelection()
    {
        ListRows list = new();
        _ = list.SetRows([Row("p9", "shell", "9"), Row("p10", "Desktop", "10"), Row("p2", "explorer", "2")]);
        _ = list.Select("p10");

        list.SortBy(1);
        Assert.Equal(["p2", "p9", "p10"], list.Rows.Select(r => r.Key));
        Assert.False(list.SortDescending);
        Assert.Equal("p10", list.SelectedKey);

        // The same column again sorts the other way; another column starts from the smallest.
        list.SortBy(1);
        Assert.Equal(["p10", "p9", "p2"], list.Rows.Select(r => r.Key));
        Assert.True(list.SortDescending);

        list.SortBy(0);
        Assert.Equal(["p10", "p2", "p9"], list.Rows.Select(r => r.Key)); // Desktop, explorer, shell: case ignored
        Assert.False(list.SortDescending);

        // New rows come in sorted.
        _ = list.SetRows([Row("z", "zsh", "1"), Row("a", "ash", "3")]);
        Assert.Equal(["a", "z"], list.Rows.Select(r => r.Key));
    }

    [Fact]
    public void EqualCellsKeepTheOrderTheyWereGivenIn()
    {
        ListRows list = new();
        _ = list.SetRows([Row("1", "tester"), Row("2", "system"), Row("3", "tester"), Row("4", "system")]);

        list.SortBy(0);

        Assert.Equal(["2", "4", "1", "3"], list.Rows.Select(r => r.Key));
    }

    [Theory]
    [InlineData(new[] { "desktop", "4", "tester" }, new[] { 10, 4, 8 }, "desktop   4   tester")]
    [InlineData(new[] { "a very long name", "12" }, new[] { 8, 4 }, "a very  12")]
    [InlineData(new[] { "name", "last column is cut too" }, new[] { 6, 8 }, "name  last col")]
    [InlineData(new[] { "only" }, new[] { 6, 4, 4 }, "only")]
    public void ARowLinesItsCellsUpInTheirColumns(string[] cells, int[] widths, string expected)
    {
        Assert.Equal(expected, ListRows.FormatRow(cells, widths));
    }
}
