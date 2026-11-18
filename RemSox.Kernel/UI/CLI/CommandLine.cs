using System.Text;

namespace RemSox.Kernel.UI.CLI;

/// <summary>
/// A command line split into words: the command name, its arguments, and the raw text after the name.
/// </summary>
/// <remarks>
/// Words are separated by whitespace. Double quotes group words ("my file.txt"), and a backslash
/// takes the next character literally (\" or \\), inside quotes or out.
/// </remarks>
public sealed class CommandLine
{
    private CommandLine(string name, List<string> arguments, string rawArguments)
    {
        Name = name;
        Arguments = arguments;
        RawArguments = rawArguments;
    }

    public string Name { get; }

    public IReadOnlyList<string> Arguments { get; }

    /// <summary>Gets the text after the command name, unsplit and untrimmed of quotes.</summary>
    public string RawArguments { get; }

    /// <summary>Splits <paramref name="line"/>, or returns null for a blank line.</summary>
    public static CommandLine? Parse(string line)
    {
        List<(string Word, int Start)> words = Split(line);
        if (words.Count == 0)
        {
            return null;
        }

        string raw = words.Count > 1 ? line[words[1].Start..].TrimEnd() : string.Empty;
        return new CommandLine(words[0].Word, words.Skip(1).Select(w => w.Word).ToList(), raw);
    }

    private static List<(string Word, int Start)> Split(string line)
    {
        List<(string, int)> words = [];
        StringBuilder current = new();
        bool inQuotes = false;
        bool inWord = false;
        int start = 0;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (!inWord && !char.IsWhiteSpace(c))
            {
                inWord = true;
                start = i;
            }

            if (c == '\\' && i + 1 < line.Length)
            {
                _ = current.Append(line[++i]);
            }
            else if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (inWord)
                {
                    words.Add((current.ToString(), start));
                    _ = current.Clear();
                    inWord = false;
                }
            }
            else
            {
                _ = current.Append(c);
            }
        }

        if (inWord)
        {
            words.Add((current.ToString(), start));
        }

        return words;
    }
}
