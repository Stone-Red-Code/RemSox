using RemSox.Shared.UI;
using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

public sealed class CheckBox() : Control("CheckBox", ThemeRole.Field)
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

    public ThemeColor TextColor
    {
        get;
        set => SetProperty(nameof(TextColor), ref field, value);
    } = ThemeRole.Text;

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        int boxSize = Size.Height;

        // Checkbox box background
        yield return new FilledRoundedRectangle
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Position = Position,
            Color = Resolve(BackgroundColor),
            Size = new Size(boxSize, boxSize),
            Radius = Theme.Radius,
        };

        // Checkbox box border
        yield return new RoundedRectangleBorder
        {
            WindowId = windowId,
            ElementId = PrimitiveId(1),
            Position = Position,
            Color = Theme[IsHovered ? ThemeRole.TextMuted : ThemeRole.Border],
            Size = new Size(boxSize, boxSize),
            Radius = Theme.Radius,
        };

        // Check mark (filled inner rect)
        if (IsChecked)
        {
            yield return new FilledRoundedRectangle
            {
                WindowId = windowId,
                ElementId = PrimitiveId(2),
                Position = new Point(Position.X + 3, Position.Y + 3),
                Color = Theme[ThemeRole.Accent],
                Size = new Size(boxSize - 6, boxSize - 6),
                Radius = Theme.Radius / 2,
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
                Color = Resolve(TextColor),
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