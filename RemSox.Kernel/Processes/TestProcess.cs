using RemSox.Kernel.Processing;
using RemSox.Kernel.Processing.IPC;
using RemSox.Kernel.UI.GUI;
using RemSox.Kernel.UI.GUI.UIEelements.Controls;
using RemSox.Kernel.UI.GUI.UIEelements.Shapes;
using RemSox.Kernel.UI.GUI.Windows;

using System.Drawing;

using Rectangle = RemSox.Kernel.UI.GUI.UIEelements.Shapes.Rectangle;

namespace RemSox.Kernel.Processes;

/// <summary>
/// A window to try the GUI's parts with: shapes (a circle under a translucent bar, a rounded
/// rectangle filled and outlined), and a <see cref="ScrollPanel"/> of more rows than fit, each
/// with a check box and a button that counts its clicks.
/// </summary>
public sealed class TestProcess() : Process("Test Process")
{
    private const int Rows = 30;
    private const int RowHeight = 26;

    internal override void Start(string[] args)
    {
        Window window = Screen.CreateWindow(this, "Test Window", new Size(320, 300));
        window.AutoFlush = true;

        _ = window.CreateUIElement<Circle>(circle =>
        {
            circle.Position = new Point(10, Window.TitleBarHeight + 6);
            circle.Radius = 12;
            circle.Color = Color.Red;
        });

        // Translucent over the circle: the circle shows through it, lightened.
        _ = window.CreateUIElement<Rectangle>(bar =>
        {
            bar.Position = new Point(6, Window.TitleBarHeight + 14);
            bar.Size = new Size(32, 8);
            bar.Color = Color.FromArgb(128, 255, 255, 255);
        });

        _ = window.CreateUIElement<Rectangle>(rounded =>
        {
            rounded.Position = new Point(222, Window.TitleBarHeight + 4);
            rounded.Size = new Size(40, 28);
            rounded.Color = ThemeRole.Accent;
            rounded.CornerRadius = 8;
        });

        _ = window.CreateUIElement<Rectangle>(outline =>
        {
            outline.Position = new Point(270, Window.TitleBarHeight + 4);
            outline.Size = new Size(40, 28);
            outline.Color = ThemeRole.BorderFocus;
            outline.IsFilled = false;
            outline.CornerRadius = 8;
        });

        _ = window.CreateUIElement<Text>(text =>
        {
            text.Position = new Point(44, Window.TitleBarHeight + 10);
            text.Content = "Scroll the rows below";
            text.FontSize = 16;
        });

        ScrollPanel panel = window.CreateUIElement<ScrollPanel>(p =>
        {
            p.Position = new Point(8, Window.TitleBarHeight + 40);
            p.Size = new Size(304, 300 - Window.TitleBarHeight - 48);
        });

        for (int i = 0; i < Rows; i++)
        {
            int row = i + 1;
            int y = (i * RowHeight) + 4;
            int clicks = 0;

            _ = panel.Add<CheckBox>(box =>
            {
                box.Position = new Point(6, y + 2);
                box.Size = new Size(18, 18);
            });

            _ = panel.Add<Text>(text =>
            {
                text.Position = new Point(32, y + 3);
                text.Content = $"Row {row}";
                text.FontSize = 16;
            });

            Button button = panel.Add<Button>(b =>
            {
                b.Position = new Point(150, y);
                b.Size = new Size(120, 22);
                b.Text = "Clicked 0";
            });
            button.OnClick += (_, _) => button.Text = $"Clicked {++clicks}";
        }

        SendMessageToAllProcesses(new TestMessage());
    }

    internal override void Tick()
    {
    }

    internal override void HandleInterProcessMessage(Message message)
    {
        if (message is TestMessage testMessage && message.SenderProcessId != Id)
        {
            Console.WriteLine($"Received message in {Name} (ID: {Id}): {testMessage.MessageText}");
        }
    }
}

internal sealed class TestMessage : Message
{
    public string MessageText { get; set; } = "Hello from TestMessage!";
}
