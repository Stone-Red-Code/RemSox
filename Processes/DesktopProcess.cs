using RemSox.Processing;
using RemSox.UI.GUI.Rendering;
using RemSox.UI.GUI.UIEelements.Controls;
using RemSox.UI.GUI.Windows;

namespace RemSox.Processes;

internal class DesktopProcess() : Process("Desktop Manager")
{
    private static bool isGraphicsInitialized = false;

    internal override void Start(string[] args)
    {
        if (!isGraphicsInitialized)
        {
            WindowManager.AddRenderSource(new CanvasRenderSource());
            isGraphicsInitialized = true;
        }

        // Force existing windows to redraw onto the new canvas renderer
        WindowManager.InvalidateAll();

        // Create a test window for new UI controls
        Window testWindow = WindowManager.CreateWindow(this, "UI Controls Test", new System.Drawing.Size(200, 180));
        testWindow.AutoFlush = true;

        Button button = testWindow.CreateUIElement<UI.GUI.UIEelements.Controls.Button>(b =>
        {
            b.Position = new System.Drawing.Point(20, 30);
            b.Size = new System.Drawing.Size(100, 30);
            b.Text = "Click Me";
            b.BackgroundColor = System.Drawing.Color.LightBlue;
        });

        button.OnClick += (s, e) =>
        {
            button.Text = "Clicked!";
            button.BackgroundColor = System.Drawing.Color.LightGreen;
        };

        CheckBox check = testWindow.CreateUIElement<UI.GUI.UIEelements.Controls.CheckBox>(c =>
        {
            c.Position = new System.Drawing.Point(20, 80);
            c.Size = new System.Drawing.Size(100, 30);
            c.Text = "Check Me";
            c.IsChecked = true;
        });

        check.OnCheckedChanged += (s, e) =>
        {
            check.Text = check.IsChecked ? "Checked!" : "Unchecked!";
        };

        _ = testWindow.CreateUIElement<UI.GUI.UIEelements.Shapes.Line>(l =>
        {
            l.Position = new System.Drawing.Point(20, 130);
            l.EndPosition = new System.Drawing.Point(180, 130);
            l.Color = System.Drawing.Color.Red;
        });
    }

    internal override void Tick()
    {
        WindowManager.Update();

        if (!ProcessManager.IsProcessRunning<TerminalProcess>())
        {
            _ = ProcessManager.SpawnProcess<TerminalProcess>();
        }
    }
}
