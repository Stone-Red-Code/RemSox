namespace RemSox.Kernel.Processing;

/// <summary>
/// Which process listens on which TCP port. Cosmos's TCP stack lets a second listener take a
/// port over (the newest one gets the connections), so every server claims its port here
/// before listening, and a port that is taken fails the start instead.
/// </summary>
/// <remarks>
/// The <see cref="ProcessManager"/> releases a process's ports when it stops, or when its start
/// fails, so a server only claims and never has to remember to release.
/// </remarks>
public static class ListeningPorts
{
    private static readonly Dictionary<int, Process> owners = [];

    /// <summary>Returns why <paramref name="port"/> cannot be listened on, or null when it is free.</summary>
    public static string? CheckFree(int port)
    {
        return owners.TryGetValue(port, out Process? owner)
            ? $"Port {port} is already used by {owner.Name} (process {owner.Id})."
            : null;
    }

    /// <summary>Records that <paramref name="process"/> listens on <paramref name="port"/>.</summary>
    /// <exception cref="InvalidOperationException">Another process listens on it.</exception>
    public static void Claim(int port, Process process)
    {
        MainLoop.AssertMainThread(nameof(Claim));

        if (owners.TryGetValue(port, out Process? owner) && owner != process)
        {
            throw new InvalidOperationException(CheckFree(port));
        }

        owners[port] = process;
    }

    /// <summary>Frees every port <paramref name="process"/> claimed.</summary>
    internal static void Release(Process process)
    {
        foreach (int port in owners.Where(entry => entry.Value == process).Select(entry => entry.Key).ToList())
        {
            _ = owners.Remove(port);
        }
    }
}
