namespace RemSox.Kernel.Processing.IPC;

public abstract class Message
{
    public int SenderProcessId { get; internal set; }
}
