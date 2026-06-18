using RemSox.Kernel.Processing;
using RemSox.Kernel.Processing.IPC;
using RemSox.Kernel.UI.GUI.UIEelements.Shapes;
using RemSox.Kernel.UI.GUI.Windows;

using System.Drawing;

namespace RemSox.Kernel.Processes;

public class TestProcess() : Process("Test Process")
{
    internal override void Start(string[] args)
    {
        Window window = WindowManager.CreateWindow(this, "Test Window", new Size(200, 150));
        window.AutoFlush = true;

        Circle circle = window.CreateUIElement<Circle>(rect =>
        {
            rect.Position = new Point(10, 10);
            rect.Radius = 50;
            rect.Color = Color.Red;
        });

        circle.Radius = 40;

        SendMessageToAllProcesses(new TestMessage { SenderProcessId = Id });
    }

    internal override void Tick()
    {
        // Example tick logic - could be used for animations, timed events, etc.
    }

    internal override void HandleInterProcessMessage(Message message)
    {
        if (message is TestMessage testMessage && message.SenderProcessId != Id)
        {
            Console.WriteLine($"Received message in {Name} (ID: {Id}): {testMessage.MessageText}");
        }
    }
}

internal class TestMessage : Message
{
    public string MessageText { get; set; } = "Hello from TestMessage!";
}