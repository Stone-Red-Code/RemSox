using Cosmos.Kernel.System.Graphics;

using RemSox.Kernel.Processing;
using RemSox.Kernel.UI.GUI.UIEelements.Controls;
using RemSox.Kernel.UI.GUI.UIEelements.Shapes;
using RemSox.Kernel.Utils;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.Windows;

/// <summary>
/// A small window in the middle of the screen that tells the user something (such as why a
/// start menu entry did not start), closed with its OK button or its close button.
/// </summary>
/// <remarks>
/// It belongs to <c>owner</c>, so it closes with it; closing it does not stop the owner as long
/// as the owner has other windows (the desktop keeps its taskbar).
/// </remarks>
public static class MessageBox
{
    private const int FontSize = 16;
    private const int LineHeight = FontSize + 4;
    private const int Width = 380;
    private const int Padding = 10;
    private const int ButtonWidth = 70;
    private const int ButtonHeight = 24;

    /// <summary>Shows <paramref name="message"/>, word-wrapped to the box's width.</summary>
    public static Window Show(Process owner, string title, string message)
    {
        int charWidth = Math.Max(1, CanvasExtensions.GetCellWidth(FontSize));
        List<string> lines = Wrap(message, (Width - (2 * Padding)) / charWidth);
        int height = Window.TitleBarHeight + Padding + (lines.Count * LineHeight) + Padding + ButtonHeight + Padding;

        Canvas canvas = Canvas.GetFullScreen();
        Point position = new(((int)canvas.Mode.Width - Width) / 2, ((int)canvas.Mode.Height - height) / 2);

        Window window = WindowManager.CreateWindow(owner, title, new Size(Width, height), position);
        window.IsResizable = false;
        window.AutoFlush = true;

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            int top = Window.TitleBarHeight + Padding + (i * LineHeight);
            _ = window.CreateUIElement<Text>(text =>
            {
                text.Position = new Point(Padding, top);
                text.Content = line;
                text.FontSize = FontSize;
                text.Color = Color.White;
            });
        }

        Button ok = window.CreateUIElement<Button>(button =>
        {
            button.Position = new Point((Width - ButtonWidth) / 2, height - Padding - ButtonHeight);
            button.Size = new Size(ButtonWidth, ButtonHeight);
            button.Text = "OK";
            button.BackgroundColor = Color.FromArgb(0, 100, 180);
            button.TextColor = Color.White;
        });

        ok.OnClick += (_, _) => WindowManager.CloseWindow(window);
        return window;
    }

    /// <summary>Breaks <paramref name="text"/> into lines of at most <paramref name="width"/> characters, at spaces where it can.</summary>
    private static List<string> Wrap(string text, int width)
    {
        List<string> lines = [];

        foreach (string paragraph in text.Split('\n'))
        {
            string line = string.Empty;

            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string rest = word;

                // A word longer than a line is cut, so a long path still fits.
                while (rest.Length > width)
                {
                    if (line.Length > 0)
                    {
                        lines.Add(line);
                        line = string.Empty;
                    }

                    lines.Add(rest[..width]);
                    rest = rest[width..];
                }

                if (line.Length == 0)
                {
                    line = rest;
                }
                else if (line.Length + 1 + rest.Length <= width)
                {
                    line += " " + rest;
                }
                else
                {
                    lines.Add(line);
                    line = rest;
                }
            }

            lines.Add(line);
        }

        return lines;
    }
}
