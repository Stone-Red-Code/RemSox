namespace RemSox.Kernel.Utils;

/// <summary>
/// Word wrapping for fixed-width text, shared by the Terminal, the text editor and message
/// boxes so they all break lines the same way.
/// </summary>
public static class TextWrap
{
    /// <summary>
    /// Where each wrapped row of <paramref name="line"/> starts. A row holds at most
    /// <paramref name="columns"/> characters; with <paramref name="keepWords"/> it breaks after
    /// its last space so words stay whole, and only a word longer than a row is split. Every
    /// line, even an empty one, has a first row at 0.
    /// </summary>
    public static List<int> RowStarts(string line, int columns, bool keepWords = true)
    {
        columns = Math.Max(1, columns);
        List<int> starts = [0];
        int start = 0;

        while (line.Length - start > columns)
        {
            // Break after the last space that fits, unless the row would then hold no text.
            int length = columns;
            if (keepWords)
            {
                int space = line.LastIndexOf(' ', start + columns - 1, columns);
                if (space > start && line[start..space].Trim().Length > 0)
                {
                    length = space - start + 1;
                }
            }

            start += length;
            starts.Add(start);
        }

        return starts;
    }

    /// <summary>
    /// Splits <paramref name="text"/> into rows of at most <paramref name="columns"/>
    /// characters, also at any line breaks it contains (see <see cref="RowStarts"/>). An empty
    /// line is one empty row.
    /// </summary>
    public static List<string> Wrap(string text, int columns, bool keepWords = true)
    {
        List<string> rows = [];

        foreach (string part in text.Split('\n'))
        {
            string line = part.TrimEnd('\r');
            List<int> starts = RowStarts(line, columns, keepWords);

            for (int i = 0; i < starts.Count; i++)
            {
                int end = i + 1 < starts.Count ? starts[i + 1] : line.Length;
                rows.Add(line[starts[i]..end]);
            }
        }

        return rows;
    }
}
