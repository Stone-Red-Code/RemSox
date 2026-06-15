using RemSox.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.UI.GUI.UIEelements.Controls;

public class CheckBox() : Control("CheckBox")
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

    public override IEnumerable<RenderCommand> ToPrimitives(int windowId)
    {
        int boxSize = Size.Height;

        // Checkbox box background
        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Type = RenderCommandType.DrawFilledRect,
            Position = Position,
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = BackgroundColor,
                ["Size"] = new Size(boxSize, boxSize),
            }
        };

        // Checkbox box border
        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(1),
            Type = RenderCommandType.DrawRectBorder,
            Position = Position,
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = Color.DarkGray,
                ["Size"] = new Size(boxSize, boxSize),
            }
        };

        // Check mark (filled inner rect)
        if (IsChecked)
        {
            yield return new RenderCommand
            {
                WindowId = windowId,
                ElementId = PrimitiveId(2),
                Type = RenderCommandType.DrawFilledRect,
                Position = new Point(Position.X + 3, Position.Y + 3),
                Properties = new Dictionary<string, object?>
                {
                    ["Color"] = Color.Black,
                    ["Size"] = new Size(boxSize - 6, boxSize - 6),
                }
            };
        }

        // Label text
        if (!string.IsNullOrEmpty(Text))
        {
            yield return new RenderCommand
            {
                WindowId = windowId,
                ElementId = PrimitiveId(3),
                Type = RenderCommandType.DrawText,
                Position = new Point(Position.X + boxSize + 5, Position.Y - 2),
                Properties = new Dictionary<string, object?>
                {
                    ["Color"] = TextColor,
                    ["Content"] = Text,
                    ["FontSize"] = boxSize,
                }
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