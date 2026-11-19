namespace RemSox.Kernel.FileSystem;

/// <summary>
/// Writes files so a crash or power loss part-way through cannot leave a half-written file in
/// place: the text goes to <c>&lt;file&gt;.new</c> first, which then replaces the file.
/// </summary>
/// <remarks>
/// The replacing move is not atomic on FAT (it can remove the old file before renaming the new
/// one), so <see cref="Recover"/> finishes or discards what an interrupted write left behind.
/// </remarks>
public static class SafeFile
{
    private const string NewSuffix = ".new";

    /// <summary>Replaces <paramref name="path"/> with <paramref name="lines"/>, creating its folder if needed.</summary>
    public static void WriteAllLines(string path, IEnumerable<string> lines)
    {
        string newPath = path + NewSuffix;

        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(newPath, lines);
        File.Move(newPath, path, overwrite: true);
    }

    /// <summary>
    /// Cleans up after a write to <paramref name="path"/> that was interrupted. With the file
    /// gone, the crash came during the final move, when the <c>.new</c> file was already
    /// complete, so it becomes the file. With both there, the crash came while the <c>.new</c>
    /// file was being written, so it is incomplete and deleted.
    /// </summary>
    /// <returns>True when the file was restored from its <c>.new</c> copy.</returns>
    public static bool Recover(string path)
    {
        string newPath = path + NewSuffix;

        if (!File.Exists(newPath))
        {
            return false;
        }

        if (File.Exists(path))
        {
            File.Delete(newPath);
            return false;
        }

        File.Move(newPath, path);
        return true;
    }
}
