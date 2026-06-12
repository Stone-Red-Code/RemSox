using System.Drawing;
using RemSox.Logging;
using RemSox.Processing.IPC;
using RemSox.UI.GUI.Windows;

namespace RemSox.Processing;

public abstract class Process(string name)
{
    public int Id { get; init; } // Will be set by ProcessManager when the process is spawned

    public ILogger Logger { protected get; init; } = null!; // Will be set by ProcessManager when the process is spawned

    public string Name { get; set; } = name;

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
