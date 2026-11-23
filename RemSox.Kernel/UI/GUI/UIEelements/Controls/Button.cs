using RemSox.Kernel.Utils;
using RemSox.Shared.UI;
using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

public sealed class Button() : Control("Button")
{
    public event EventHandler? OnClick;

    public string Text
    {
        get;
        set => SetProperty(nameof(Text), ref field, value);
    } = string.Empty;

    public Color TextColor
    {
        get;
        set => SetProperty(nameof(TextColor), ref field, value);
    } = Color.Black;

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        // Background fill
        yield return new FilledRectangle
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Position = Position,
            Color = BackgroundColor,
            Size = Size,
        };

        // Border
        yield return new RectangleBorder
        {
            WindowId = windowId,
            ElementId = PrimitiveId(1),
            Position = Position,
            Color = Color.DarkGray,
            Size = Size,
        };

        // Text (centered, falls back to left-aligned if too wide)
        if (!string.IsNullOrEmpty(Text))
        {
            int fontSize = Size.Height - 8;
            int charWidth = Math.Max(1, CanvasExtensions.GetCellWidth(fontSize));
            int maxChars = Size.Width / charWidth;
            string display = Text;
            if (display.Length > maxChars && maxChars > 0)
            {
                display = Text[..maxChars];
            }
            int textWidth = display.Length * charWidth;
            int tx = textWidth >= Size.Width
                ? Position.X
                : Position.X + ((Size.Width - textWidth) / 2);
            int ty = Position.Y + ((Size.Height - fontSize) / 2);

            yield return new TextPrimitive
            {
                WindowId = windowId,
                ElementId = PrimitiveId(2),
                Position = new Point(tx, ty),
                Color = TextColor,
                Content = display,
                FontSize = fontSize,
                MaxWidth = Size.Width,
            };
        }
    }

    public override void HandleMouseEvent(MouseEvent mouseEvent)
    {
        if (mouseEvent.Type == MouseEventType.ButtonDown && mouseEvent.Button == MouseButton.Left)
        {
            OnClick?.Invoke(this, EventArgs.Empty);
        }
    }
}