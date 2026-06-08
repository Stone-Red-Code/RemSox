using System;
using RemSox.Processing.IPC;

namespace RemSox.Processing;

public abstract class Process(string name)
{
    public int Id { get; init; }

    public string Name { get; set; } = name;

    public bool StopRequested { get; private set; } = false;

    internal abstract void Run();

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
