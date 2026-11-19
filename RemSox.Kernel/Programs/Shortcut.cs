using RemSox.Kernel.FileSystem;
using RemSox.Kernel.Logging;
using RemSox.Kernel.UI.CLI;

namespace RemSox.Kernel.Programs;

/// <summary>
/// A shortcut: a small text file (<c>.rsl</c>, "RemSox link") saying what to start, like a
/// Windows <c>.lnk</c> or a Linux <c>.desktop</c> file. The start menu shows one entry per
/// shortcut in <see cref="SystemPaths.StartMenuFolder"/>.
/// </summary>
/// <remarks>
/// One <c>key = value</c> per line, <c>#</c> starting a comment, unknown keys ignored so newer
/// fields (an icon, say) do not break older systems:
/// <code>
/// title = Say hello
/// run = hello
/// args = --loud "two words"
/// folder = /home
/// </code>
/// <c>run</c> is the only required field. It is resolved like a name typed in a shell: a program,
/// else a file found in <c>folder</c> or the <c>path</c> folders, opened by its file association.
/// <c>args</c> is split like a shell line. <c>title</c> defaults to the file name; <c>folder</c>
/// to the starter's folder. Opening a shortcut anywhere (typing its name in a shell, the start
/// menu) starts it; like a Windows <c>.lnk</c>, it has no file association.
/// </remarks>
public sealed class Shortcut(string title, string run, string arguments, string? folder)
{
    public const string FileExtension = ".rsl";

    /// <summary>
    /// The shortcuts a new start menu folder starts with. The Terminal has none: the start menu
    /// always shows it, so a shell can be opened whatever these files say.
    /// </summary>
    private static readonly (string FileName, Shortcut Shortcut)[] Defaults =
    [
        ("test" + FileExtension, new Shortcut("Test Process", "test", string.Empty, null)),
    ];

    /// <summary>Gets the name shown for the shortcut.</summary>
    public string Title { get; } = title;

    /// <summary>Gets the program or script to start.</summary>
    public string Run { get; } = run;

    /// <summary>Gets the arguments, as typed on a shell line.</summary>
    public string Arguments { get; } = arguments;

    /// <summary>Gets the folder to start in, or null for the starter's own.</summary>
    public string? Folder { get; } = folder;

    // Set while a shortcut starts, so one whose target is a shortcut (itself, say) fails instead
    // of starting shortcuts forever. Starting is synchronous, and there is only one core.
    private static bool starting;

    /// <summary>
    /// Starts the shortcut's target through <see cref="Launcher.Start"/>, with
    /// <paramref name="extraArguments"/> after its own, as Windows does with a <c>.lnk</c>.
    /// </summary>
    public ProgramStart Start(string defaultFolder, IReadOnlyList<string>? extraArguments = null)
    {
        if (starting)
        {
            return ProgramStart.Fail($"A shortcut cannot start another shortcut ({Title}).");
        }

        starting = true;
        try
        {
            return Launcher.Start(Run, [.. CommandLine.SplitWords(Arguments), .. extraArguments ?? []], Folder ?? defaultFolder);
        }
        finally
        {
            starting = false;
        }
    }

    /// <summary>Reads the shortcut file at <paramref name="path"/> and starts it.</summary>
    public static ProgramStart StartFile(string path, IReadOnlyList<string> extraArguments, string defaultFolder)
    {
        if (!File.Exists(path))
        {
            return ProgramStart.Fail("Shortcut not found: " + path);
        }

        Shortcut? shortcut;
        string? error;

        try
        {
            shortcut = Read(path, out error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ProgramStart.Fail($"Cannot read {path}: {ex.Message}");
        }

        return shortcut?.Start(defaultFolder, extraArguments) ?? ProgramStart.Fail($"{path} is no valid shortcut: {error}");
    }

    /// <summary>Reads a shortcut file.</summary>
    /// <returns>The shortcut, or null with <paramref name="error"/> saying why the file is not one.</returns>
    public static Shortcut? Read(string path, out string? error)
    {
        Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);

        foreach (string line in File.ReadAllLines(path))
        {
            string trimmed = line.Trim();
            int equals = trimmed.IndexOf('=');

            if (trimmed.Length > 0 && trimmed[0] != '#' && equals > 0)
            {
                fields[trimmed[..equals].Trim()] = trimmed[(equals + 1)..].Trim();
            }
        }

        string? folder = fields.GetValueOrDefault("folder");
        if (string.IsNullOrEmpty(fields.GetValueOrDefault("run")))
        {
            error = "it has no 'run = ...' line.";
            return null;
        }

        if (!string.IsNullOrEmpty(folder) && !folder.StartsWith('/'))
        {
            error = "its folder is not an absolute path.";
            return null;
        }

        error = null;
        string title = fields.GetValueOrDefault("title") is { Length: > 0 } named ? named : Path.GetFileNameWithoutExtension(path);
        return new Shortcut(title, fields["run"], fields.GetValueOrDefault("args") ?? string.Empty, string.IsNullOrEmpty(folder) ? null : folder);
    }

    /// <summary>
    /// Reads every shortcut in the start menu folder, sorted by title. Files that are no valid
    /// shortcut are logged and left out; without a disk the menu is empty.
    /// </summary>
    public static List<Shortcut> ReadStartMenu()
    {
        List<Shortcut> shortcuts = [];

        try
        {
            foreach (string path in Directory.EnumerateFiles(SystemPaths.StartMenuFolder))
            {
                if (!path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Shortcut? shortcut;
                string? error;

                try
                {
                    shortcut = Read(path, out error);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    shortcut = null;
                    error = ex.Message;
                }

                if (shortcut is not null)
                {
                    shortcuts.Add(shortcut);
                }
                else
                {
                    SystemLog.Log($"{path} is left out of the start menu: {error}", LogSeverity.Warning);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SystemLog.Log("Could not read the start menu: " + ex.Message, LogSeverity.Error);
        }

        return [.. shortcuts.OrderBy(shortcut => shortcut.Title, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Writes the default shortcuts into a newly created start menu folder.</summary>
    public static void WriteDefaults()
    {
        foreach ((string fileName, Shortcut shortcut) in Defaults)
        {
            SafeFile.WriteAllLines(Path.Combine(SystemPaths.StartMenuFolder, fileName), shortcut.ToLines());
        }
    }

    private IEnumerable<string> ToLines()
    {
        yield return "title = " + Title;
        yield return "run = " + Run;

        if (Arguments.Length > 0)
        {
            yield return "args = " + Arguments;
        }

        if (Folder is not null)
        {
            yield return "folder = " + Folder;
        }
    }
}
