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

    public ThemeColor TextColor
    {
        get;
        set => SetProperty(nameof(TextColor), ref field, value);
    } = ThemeRole.Text;

    /// <summary>Gets or sets the text's height in pixels; 0 (the default) for the button's height less 8.</summary>
    public int FontSize
    {
        get;
        set => SetProperty(nameof(FontSize), ref field, value);
    }

    /// <summary>Gets or sets whether the text is centred (the default), or starts at the left as in a list or a menu.</summary>
    public bool CentersText
    {
        get;
        set => SetProperty(nameof(CentersText), ref field, value);
    } = true;

    /// <summary>The space before left-aligned text.</summary>
    private const int TextPadding = 10;

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        // Flat and rounded, with no frame: the fill alone shows the button and its state.
        yield return new FilledRoundedRectangle
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Position = Position,
            Color = Theme.GetInteractive(BackgroundColor, IsHovered, IsPressed),
            Size = Size,
            Radius = Theme.Radius,
        };

        // Text (centered, falls back to left-aligned if too wide)
        if (!string.IsNullOrEmpty(Text))
        {
            int fontSize = FontSize > 0 ? FontSize : Size.Height - 8;
            int charWidth = Math.Max(1, CanvasExtensions.GetCellWidth(fontSize));
            int padding = CentersText ? 0 : TextPadding;
            int maxChars = (Size.Width - (2 * padding)) / charWidth;
            string display = Text;
            if (display.Length > maxChars && maxChars > 0)
            {
                display = Text[..maxChars];
            }
            int textWidth = display.Length * charWidth;
            int tx = !CentersText ? Position.X + padding
                : textWidth >= Size.Width ? Position.X
                : Position.X + ((Size.Width - textWidth) / 2);
            int ty = Position.Y + ((Size.Height - fontSize) / 2);

            yield return new TextPrimitive
            {
                WindowId = windowId,
                ElementId = PrimitiveId(2),
                Position = new Point(tx, ty),
                Color = Resolve(TextColor),
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