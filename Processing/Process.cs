using RemSox.Logging;
using RemSox.Processing.IPC;

namespace RemSox.Processing;

public abstract class Process(string name)
{
    public int Id { get; init; } // Will be set by ProcessManager when the process is spawned

    public ILogger Logger { protected get; init; } = null!; // Will be set by ProcessManager when the process is spawned

    public string Name { get; set; } = name;

    public bool StopRequested { get; private set; } = false;

    public bool IsRunning => !StopRequested;

    internal abstract void Run(string[] args);

    internal virtual void HandleInterProcessMessage(Message message)
    {
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
