using RemSox.Kernel.Accounts;
using RemSox.Kernel.Logging;
using RemSox.Kernel.Processing.IPC;
using RemSox.Kernel.UI.GUI.Windows;

using System.Drawing;

namespace RemSox.Kernel.Processing;

public abstract class Process(string name)
{
    public int Id { get; init; } // Will be set by ProcessManager when the process is spawned

    public ILogger Logger { protected get; init; } = null!; // Will be set by ProcessManager when the process is spawned

    public string Name { get; set; } = name;

    /// <summary>
    /// Gets or sets the folder this process resolves relative paths against, and where shells
    /// inside it start. It begins as the folder of whatever started the process (see
    /// <see cref="ProcessManager.SpawnProcess{T}"/>); the process may change it afterwards.
    /// </summary>
    public string WorkingDirectory { get; set; } = "/";

    /// <summary>
    /// Gets the logon session the process belongs to, and so the account it runs as: the
    /// session of whatever started it, or the system's for the kernel's own processes and
    /// services. Ending the session stops the process.
    /// </summary>
    public LogonSession Session { get; init; } = LogonSessions.System;

    /// <summary>
    /// Gets whether the process was started elevated (by <c>sudo</c>, or by an elevated process):
    /// an administrator's process then has admin rights, see <see cref="Caller"/>.
    /// </summary>
    public bool Elevated { get; init; }

    /// <summary>Gets who the process acts as when it asks for something: its session, elevated or not.</summary>
    public Caller Caller => new(Session, Elevated);

    public bool StopRequested { get; private set; } = false;

    public bool IsRunning => !StopRequested;

    internal virtual void Start(string[] args) { }

    internal abstract void Tick();

    internal virtual void Stop() { }

    internal virtual void HandleInterProcessMessage(Message message)
    {
    }

    protected Window CreateWindow(string title, Size size)
    {
        return WindowManager.CreateWindow(this, title, size);
    }

    protected Window CreateWindow(string title, Size size, Point position)
    {
        return WindowManager.CreateWindow(this, title, size, position);
    }

    protected void SendMessageToProcess(int targetProcessId, Message message)
    {
        InterProcessCommunicator.Send(targetProcessId, message, this);
    }

    protected void SendMessageToAllProcesses(Message message)
    {
        InterProcessCommunicator.SendToAll(message, this);
    }

    internal void RequestStop()
    {
        StopRequested = true;
    }
}
