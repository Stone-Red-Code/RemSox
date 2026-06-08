namespace RemSox.Processing.IPC;

internal static class InterProcessCommunicator
{
    public static void Send(int targetProcessId, Message message, Process sender)
    {
        message.SenderProcessId = sender.Id; // automatically set
        ProcessManager.GetProcess(targetProcessId)?.HandleInterProcessMessage(message);
    }

    public static void SendToAll(Message message, Process sender)
    {
        message.SenderProcessId = sender.Id; // automatically set

        foreach (Process process in ProcessManager.GetAllProcesses())
        {
            process.HandleInterProcessMessage(message);
        }
    }
}
