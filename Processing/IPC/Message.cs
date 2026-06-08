namespace RemSox.Processing.IPC;

public abstract class Message
{
    public int SenderProcessId { get; internal set; }
}
