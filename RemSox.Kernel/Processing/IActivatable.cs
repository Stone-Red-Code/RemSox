namespace RemSox.Kernel.Processing;

/// <summary>
/// A process that, being one per session (<see cref="ProcessManifest.ProcessManifestFlags.OnePerSession"/>),
/// comes to the front when started again in its session, as the Task Manager does on Windows,
/// rather than being refused.
/// </summary>
public interface IActivatable
{
    /// <summary>Shows the process's window in front, restored if it was minimized.</summary>
    void Activate();
}
