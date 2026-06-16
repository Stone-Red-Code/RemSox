using RemSox.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.UI.GUI.UIEelements.Controls;

public class RadioButton() : Control("RadioButton")
{
    public event EventHandler? OnCheckedChanged;

    public bool IsChecked
    {
        get;
        set
        {
            SetProperty(nameof(IsChecked), ref field, value);
            OnCheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

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

    public override IEnumerable<RenderCommand> ToPrimitives(int windowId)
    {
        int diameter = Size.Height;
        int radius = diameter / 2;
        Point center = new(Position.X + radius, Position.Y + radius);

        // Outer circle
        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Type = RenderCommandType.DrawCircle,
            Position = new Point(center.X - radius, center.Y - radius),
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = Color.DarkGray,
                ["Radius"] = radius,
            }
        };

        // Inner fill when checked
        if (IsChecked)
        {
            int innerRadius = radius - 3;
            if (innerRadius > 0)
            {
                yield return new RenderCommand
                {
                    WindowId = windowId,
                    ElementId = PrimitiveId(1),
                    Type = RenderCommandType.DrawFilledCircle,
                    Position = new Point(center.X - innerRadius, center.Y - innerRadius),
                    Properties = new Dictionary<string, object?>
                    {
                        ["Color"] = Color.Black,
                        ["Radius"] = innerRadius,
                    }
                };
            }
        }

        // Label text
        if (!string.IsNullOrEmpty(Text))
        {
            int textX = Position.X + diameter + 5;
            int textY = Position.Y + (Size.Height / 2) - 8;

            yield return new RenderCommand
            {
                WindowId = windowId,
                ElementId = PrimitiveId(2),
                Type = RenderCommandType.DrawText,
                Position = new Point(textX, textY),
                Properties = new Dictionary<string, object?>
                {
                    ["Color"] = TextColor,
                    ["Content"] = Text,
                    ["FontSize"] = Size.Height,
                    ["MaxWidth"] = Size.Width - Size.Height - 5,
                }
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
