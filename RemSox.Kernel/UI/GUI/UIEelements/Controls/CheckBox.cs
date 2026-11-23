using RemSox.Shared.UI;
using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

public sealed class CheckBox() : Control("CheckBox")
{
    public event EventHandler? OnCheckedChanged;

    public bool IsChecked
    {
        get;
        set
        {
            if (value != field)
            {
                SetProperty(nameof(IsChecked), ref field, value);
                OnCheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    } = false;

    public string Text
    {
        get;
        set => SetProperty(nameof(Text), ref field, value);
    } = string.Empty;

    public Color TextColor
    {
        get;
        set => SetProperty(nameof(TextColor), ref field, value);
    } = Color.White;

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        int boxSize = Size.Height;

        // Checkbox box background
        yield return new FilledRectangle
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Position = Position,
            Color = BackgroundColor,
            Size = new Size(boxSize, boxSize),
        };

        // Checkbox box border
        yield return new RectangleBorder
        {
            WindowId = windowId,
            ElementId = PrimitiveId(1),
            Position = Position,
            Color = Color.DarkGray,
            Size = new Size(boxSize, boxSize),
        };

        // Check mark (filled inner rect)
        if (IsChecked)
        {
            yield return new FilledRectangle
            {
                WindowId = windowId,
                ElementId = PrimitiveId(2),
                Position = new Point(Position.X + 3, Position.Y + 3),
                Color = Color.Black,
                Size = new Size(boxSize - 6, boxSize - 6),
            };
        }

        // Label text
        if (!string.IsNullOrEmpty(Text))
        {
            yield return new TextPrimitive
            {
                WindowId = windowId,
                ElementId = PrimitiveId(3),
                Position = new Point(Position.X + boxSize + 5, Position.Y - 2),
                Color = TextColor,
                Content = Text,
                FontSize = boxSize,
                MaxWidth = Size.Width - boxSize - 5,
            };
        }
    }

    public override void HandleMouseEvent(MouseEvent mouseEvent)
    {
        if (mouseEvent.Type == MouseEventType.ButtonDown && mouseEvent.Button == MouseButton.Left)
        {
            IsChecked = !IsChecked;
        }
    }
}