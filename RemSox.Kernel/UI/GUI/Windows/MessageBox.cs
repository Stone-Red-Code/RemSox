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
        List<string> lines = TextWrap.Wrap(message, (Width - (2 * Padding)) / charWidth);
        int height = Window.TitleBarHeight + Padding + (lines.Count * LineHeight) + Padding + ButtonHeight + Padding;

        Size screen = Screen.GetScreenBounds().Size;
        Point position = new((screen.Width - Width) / 2, (screen.Height - height) / 2);

        Window window = owner.Screen.CreateWindow(owner, title, new Size(Width, height), position);
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

        ok.OnClick += (_, _) => window.Screen.CloseWindow(window);
        return window;
    }
}
