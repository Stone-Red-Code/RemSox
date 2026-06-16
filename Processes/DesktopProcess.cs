using RemSox.Processing;
using RemSox.UI.GUI.Layout;
using RemSox.UI.GUI.Rendering;
using RemSox.UI.GUI.UIEelements.Controls;
using RemSox.UI.GUI.Windows;

using System.Drawing;

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

        WindowManager.InvalidateAll();

        // Test window 1: interactive controls using stack layout
        Window testWindow = WindowManager.CreateWindow(this, "UI Controls", new Size(280, 260));
        testWindow.AutoFlush = true;

        StackLayout stack = testWindow.CreateStackLayout(20, 30, 6);
        stack.UniformWidth = 240;

        Button button = stack.Add<Button>(b =>
        {
            b.Size = new Size(240, 25);
            b.Text = "Click Me";
            b.BackgroundColor = Color.LightBlue;
        });

        button.OnClick += (s, e) =>
        {
            button.Text = "Clicked!";
            button.BackgroundColor = Color.LightGreen;
        };

        CheckBox check = stack.Add<CheckBox>(c =>
        {
            c.Size = new Size(240, 20);
            c.Text = "Check Me";
            c.IsChecked = true;
        });

        check.OnCheckedChanged += (s, e) =>
        {
            check.Text = check.IsChecked ? "Checked!" : "Unchecked!";
        };

        RadioButton radio = stack.Add<RadioButton>(r =>
        {
            r.Size = new Size(240, 20);
            r.Text = "Radio Option";
            r.IsChecked = true;
        });

        Slider slider = stack.Add<Slider>(s =>
        {
            s.Size = new Size(240, 24);
            s.BackgroundColor = Color.SteelBlue;
            s.Value = 60;
        });

        ProgressBar progress = stack.Add<ProgressBar>(p =>
        {
            p.Size = new Size(240, 20);
            p.BackgroundColor = Color.DimGray;
            p.FillColor = Color.LimeGreen;
            p.Value = 60;
        });

        slider.OnValueChanged += (_, _) =>
        {
            progress.Value = slider.Value;
        };

        // Test window 2: shapes and panel
        Window shapesWin = WindowManager.CreateWindow(this, "Shapes & Panel", new Size(200, 220));
        shapesWin.AutoFlush = true;

        _ = shapesWin.CreateUIElement<Panel>(p =>
        {
            p.Position = new Point(10, 25);
            p.Size = new Size(180, 80);
            p.BackgroundColor = Color.FromArgb(48, 48, 48);
        });

        _ = shapesWin.CreateUIElement<UI.GUI.UIEelements.Shapes.Circle>(c =>
        {
            c.Position = new Point(20, 35);
            c.Radius = 10;
            c.Color = Color.Coral;
        });

        _ = shapesWin.CreateUIElement<UI.GUI.UIEelements.Shapes.Rectangle>(r =>
        {
            r.Position = new Point(60, 35);
            r.Size = new Size(50, 30);
            r.Color = Color.CornflowerBlue;
            r.IsFilled = true;
        });

        _ = shapesWin.CreateUIElement<UI.GUI.UIEelements.Shapes.Text>(t =>
        {
            t.Position = new Point(10, 120);
            t.Content = "Hello from the desktop!";
            t.Color = Color.White;
            t.FontSize = 14;
        });

        _ = shapesWin.CreateUIElement<UI.GUI.UIEelements.Shapes.Line>(l =>
        {
            l.Position = new Point(10, 150);
            l.EndPosition = new Point(180, 180);
            l.Color = Color.Orange;
        });

        CheckBox shapesCheck = shapesWin.CreateUIElement<CheckBox>(c =>
        {
            c.Position = new Point(10, 185);
            c.Size = new Size(180, 20);
            c.Text = "Toggle";
        });

        shapesCheck.OnCheckedChanged += (_, _) => { };

        // Test window 3: grid layout
        Window gridWin = WindowManager.CreateWindow(this, "Grid Layout", new Size(260, 160));
        gridWin.AutoFlush = true;

        GridLayout grid = gridWin.CreateGridLayout(10, 25,
            new int[] { 70, 70, 70 },
            new int[] { 25, 25, 25 },
            6);

        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                int r = row, c = col;
                Button btn = grid.Add<Button>(col, row, b =>
                {
                    b.Text = $"[{c},{r}]";
                    b.BackgroundColor = (c + r) % 2 == 0 ? Color.SteelBlue : Color.DimGray;
                });

                btn.OnClick += (_, _) =>
                {
                    btn.Text = "X";
                };
            }
        }
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
