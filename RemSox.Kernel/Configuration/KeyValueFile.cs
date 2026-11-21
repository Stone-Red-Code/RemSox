namespace RemSox.Kernel.Configuration;

/// <summary>
/// The lines of a <c>key = value</c> file (settings, shortcuts): <c>#</c> starts a comment, keys
/// are compared in lower case, and the last line that sets a key wins. It keeps every line, so a
/// change rewrites only the line it touches and comments and the order of the file survive.
/// Reading and saving the file is its owner's job, since who may write it differs by file.
/// </summary>
public sealed class KeyValueFile(IEnumerable<string> lines)
{
    private readonly List<string> lines = [.. lines];

    /// <summary>Gets the file's lines, to save them.</summary>
    public IReadOnlyList<string> Lines => lines;

    /// <summary>Gets every <c>key = value</c> line, in order; a key may appear more than once.</summary>
    public IEnumerable<(string Key, string Value)> Entries => lines.Select(ParseLine).Where(e => e is not null).Select(e => e!.Value);

    /// <summary>Returns the value of the last line that sets <paramref name="key"/>, or null when none does.</summary>
    public string? Get(string key)
    {
        string? value = null;
        foreach ((string entryKey, string entryValue) in Entries)
        {
            if (entryKey == key)
            {
                value = entryValue;
            }
        }

        return value;
    }

    /// <summary>Returns whether a line sets <paramref name="key"/>.</summary>
    public bool Contains(string key)
    {
        return Entries.Any(e => e.Key == key);
    }

    /// <summary>Sets <paramref name="key"/> in its first line (removing any later ones), or adds a line at the end.</summary>
    public void Set(string key, string value)
    {
        string line = value.Length > 0 ? $"{key} = {value}" : $"{key} =";
        int index = lines.FindIndex(l => ParseLine(l)?.Key == key);

        if (index < 0)
        {
            lines.Add(line);
            return;
        }

        lines[index] = line;
        for (int i = lines.Count - 1; i > index; i--)
        {
            if (ParseLine(lines[i])?.Key == key)
            {
                lines.RemoveAt(i);
            }
        }
    }

    /// <summary>Removes every line that sets <paramref name="key"/>.</summary>
    /// <returns>Whether there was one.</returns>
    public bool Remove(string key)
    {
        return lines.RemoveAll(l => ParseLine(l)?.Key == key) > 0;
    }

    /// <summary>Adds lines at the end, as they are (comments, new settings).</summary>
    public void Append(IEnumerable<string> newLines)
    {
        lines.AddRange(newLines);
    }

    /// <summary>Returns a copy, to go back to if saving a change fails.</summary>
    public KeyValueFile Copy()
    {
        return new KeyValueFile(lines);
    }

    /// <summary>Splits a <c>key = value</c> line; null for blank lines, comments and lines without '='.</summary>
    public static (string Key, string Value)? ParseLine(string line)
    {
        string trimmed = line.Trim();
        int equals = trimmed.IndexOf('=');

        if (trimmed.Length == 0 || trimmed[0] == '#' || equals <= 0)
        {
            return null;
        }

        return (trimmed[..equals].Trim().ToLowerInvariant(), trimmed[(equals + 1)..].Trim());
    }
}
