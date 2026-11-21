using System.Diagnostics;
using System.Globalization;

using RemSox.Kernel.Processing;

namespace RemSox.Kernel.Accounts;

/// <summary>
/// Slows down password guessing from the network (Telnet, remote desktop), across all
/// connections: a delay that waits per connection is undone by reconnecting or by opening
/// several at once. Asked before a password is checked, so a refused try costs no PBKDF2 time
/// on the main loop.
/// </summary>
/// <remarks>
/// <para>
/// Two limits. Per name: after <see cref="FreeFailures"/> failures in a row, each try must wait
/// longer (2 s, doubling, up to <see cref="MaxNameDelay"/>), until a login with that name
/// succeeds or it has had no failure for <see cref="ForgetAfter"/>. Names without an account
/// count alike, so the answer does not tell which names exist. Overall: failures use up a
/// budget of <see cref="Burst"/> that refills by one every <see cref="Refill"/>; while it is
/// empty, no network login is tried, which also bounds how long failed checks can stall the
/// main loop.
/// </para>
/// <para>
/// Someone who keeps guessing therefore also keeps that account (or, guessing many names,
/// everyone) from logging in over the network, though never for longer than the delays above
/// after they stop. Logins at the machine are not limited here, so its owner can always get in.
/// </para>
/// </remarks>
public static class LoginThrottle
{
    /// <summary>Failures of a name before its tries must wait.</summary>
    public const int FreeFailures = 3;

    /// <summary>The overall number of failures allowed at once, before they must wait for <see cref="Refill"/>.</summary>
    public const int Burst = 5;

    /// <summary>The longest wait for one name.</summary>
    public static readonly TimeSpan MaxNameDelay = TimeSpan.FromMinutes(5);

    /// <summary>How long a name is remembered after its last failure.</summary>
    public static readonly TimeSpan ForgetAfter = TimeSpan.FromMinutes(15);

    /// <summary>How long the overall budget takes to win back one failure.</summary>
    public static readonly TimeSpan Refill = TimeSpan.FromSeconds(10);

    // Enough for real use; someone trying many names makes the oldest ones forgotten instead of
    // filling memory.
    private const int MaxNames = 256;

    private static readonly Dictionary<string, NameFailures> names = [];

    private static double budget = Burst;
    private static long budgetAt = Stopwatch.GetTimestamp();

    /// <summary>
    /// Gets or sets where the time comes from, a <see cref="Stopwatch"/> timestamp; tests replace
    /// it so they need not wait out the real delays.
    /// </summary>
    internal static Func<long> Clock { get; set; } = Stopwatch.GetTimestamp;

    /// <summary>Forgets every failure and fills the overall budget, as at boot (tests).</summary>
    internal static void Reset()
    {
        names.Clear();
        budget = Burst;
        budgetAt = Clock();
    }

    /// <summary>
    /// Returns why a network login as <paramref name="name"/> may not be tried now (to show the
    /// client), or null when it may.
    /// </summary>
    public static string? Refuse(string name)
    {
        MainLoop.AssertMainThread(nameof(Refuse));

        long now = Clock();
        TimeSpan wait = TimeSpan.Zero;

        UpdateBudget(now);
        if (budget < 1)
        {
            wait = (1 - budget) * Refill;
        }

        if (Find(name, now) is { } failures && failures.Count >= FreeFailures)
        {
            TimeSpan left = GetDelay(failures.Count) - Stopwatch.GetElapsedTime(failures.Last, now);
            if (left > wait)
            {
                wait = left;
            }
        }

        return wait > TimeSpan.Zero
            ? $"Too many failed logins; try again in {Math.Ceiling(wait.TotalSeconds).ToString(CultureInfo.InvariantCulture)} s."
            : null;
    }

    /// <summary>Counts a wrong name or password given from the network.</summary>
    public static void Failed(string name)
    {
        MainLoop.AssertMainThread(nameof(Failed));

        long now = Clock();
        UpdateBudget(now);
        budget = Math.Max(budget - 1, 0);

        string key = Key(name);
        int count = Find(name, now) is { } failures ? failures.Count + 1 : 1;
        names[key] = new NameFailures(count, now);

        if (names.Count > MaxNames)
        {
            Prune(now);
        }
    }

    /// <summary>Forgets the failures of a name whose right password was given.</summary>
    public static void Succeeded(string name)
    {
        MainLoop.AssertMainThread(nameof(Succeeded));
        _ = names.Remove(Key(name));
    }

    /// <summary>The wait after <paramref name="failures"/> failures in a row (at least <see cref="FreeFailures"/>).</summary>
    private static TimeSpan GetDelay(int failures)
    {
        int doublings = Math.Min(failures - FreeFailures, 16);
        TimeSpan delay = TimeSpan.FromSeconds(2 << doublings);
        return delay < MaxNameDelay ? delay : MaxNameDelay;
    }

    private static NameFailures? Find(string name, long now)
    {
        string key = Key(name);
        if (!names.TryGetValue(key, out NameFailures failures))
        {
            return null;
        }

        if (Stopwatch.GetElapsedTime(failures.Last, now) > ForgetAfter)
        {
            _ = names.Remove(key);
            return null;
        }

        return failures;
    }

    private static void UpdateBudget(long now)
    {
        budget = Math.Min(Burst, budget + (Stopwatch.GetElapsedTime(budgetAt, now) / Refill));
        budgetAt = now;
    }

    /// <summary>Forgets names not failed for <see cref="ForgetAfter"/>, then the oldest ones until few enough are left.</summary>
    private static void Prune(long now)
    {
        foreach (string key in names.Where(entry => Stopwatch.GetElapsedTime(entry.Value.Last, now) > ForgetAfter).Select(entry => entry.Key).ToList())
        {
            _ = names.Remove(key);
        }

        foreach (string key in names.OrderBy(entry => entry.Value.Last).Take(names.Count - MaxNames).Select(entry => entry.Key).ToList())
        {
            _ = names.Remove(key);
        }
    }

    private static string Key(string name)
    {
        return name.Trim().ToLowerInvariant();
    }

    /// <param name="Count">Failures in a row.</param>
    /// <param name="Last">When the last one was (a <see cref="Stopwatch"/> timestamp).</param>
    private readonly record struct NameFailures(int Count, long Last);
}
