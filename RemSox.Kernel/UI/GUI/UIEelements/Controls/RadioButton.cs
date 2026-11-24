using RemSox.Shared.UI;
using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

public sealed class RadioButton() : Control("RadioButton", ThemeRole.Field)
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
    }

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
        int diameter = Size.Height;
        int radius = diameter / 2;
        Point center = new(Position.X + radius, Position.Y + radius);

        // Outer circle
        yield return new CircleOutline
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Position = new Point(center.X - radius, center.Y - radius),
            Color = Theme[IsHovered ? ThemeRole.TextMuted : ThemeRole.Border],
            Radius = radius,
        };

        // Inner fill when checked
        if (IsChecked)
        {
            int innerRadius = radius - 3;
            if (innerRadius > 0)
            {
                yield return new FilledCircle
                {
                    WindowId = windowId,
                    ElementId = PrimitiveId(1),
                    Position = new Point(center.X - innerRadius, center.Y - innerRadius),
                    Color = Theme[ThemeRole.Accent],
                    Radius = innerRadius,
                };
            }
        }

        // Label text
        if (!string.IsNullOrEmpty(Text))
        {
            int textX = Position.X + diameter + 5;
            int textY = Position.Y + (Size.Height / 2) - 8;

            yield return new TextPrimitive
            {
                WindowId = windowId,
                ElementId = PrimitiveId(2),
                Position = new Point(textX, textY),
                Color = Resolve(TextColor),
                Content = Text,
                FontSize = Size.Height,
                MaxWidth = Size.Width - Size.Height - 5,
            };
        }
    }

    public override void HandleMouseEvent(MouseEvent mouseEvent)
    {
        if (mouseEvent.Type == MouseEventType.ButtonDown && mouseEvent.Button == MouseButton.Left)
        {
            IsChecked = true;
        }
    }
}
