namespace RemSox.Kernel.Processing;

/// <summary>
/// Runs slow computation (hashing a password, the remote desktop login's arithmetic) on a thread
/// of its own, so the main loop keeps running meanwhile: everything else would otherwise stop
/// for the second or two it takes in QEMU.
/// </summary>
/// <remarks>
/// The work must be pure computation on what it was given: it may touch none of the system's
/// state, which only the main loop owns. Awaited on the main loop, the result arrives back there
/// (<see cref="MainLoop"/> is the synchronization context). Cosmos's scheduler is preemptive, so
/// the thread shares the CPU with the main loop rather than blocking it.
/// </remarks>
public static class BackgroundWork
{
    /// <summary>Starts <paramref name="work"/> on a new thread; the task completes with its result or exception.</summary>
    public static Task<T> Run<T>(Func<T> work)
    {
        TaskCompletionSource<T> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        thread.Start();
        return done.Task;
    }
}
