using RemSox.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.UI.GUI.UIEelements.Controls;

public class Button() : Control("Button")
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

    public override IEnumerable<RenderCommand> ToPrimitives(int windowId)
    {
        // Background fill
        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Type = RenderCommandType.DrawFilledRect,
            Position = Position,
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = BackgroundColor,
                ["Size"] = Size,
            }
        };

        // Border
        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(1),
            Type = RenderCommandType.DrawRectBorder,
            Position = Position,
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = Color.DarkGray,
                ["Size"] = Size,
            }
        };

        // Text (centered)
        if (!string.IsNullOrEmpty(Text))
        {
            int tx = Position.X + (Size.Width / 2) - (Text.Length * 4);
            int ty = Position.Y + (Size.Height / 2) - 8;

            yield return new RenderCommand
            {
                WindowId = windowId,
                ElementId = PrimitiveId(2),
                Type = RenderCommandType.DrawText,
                Position = new Point(tx, ty),
                Properties = new Dictionary<string, object?>
                {
                    ["Color"] = TextColor,
                    ["Content"] = Text,
                    ["FontSize"] = Size.Height - 8,
                    ["MaxWidth"] = Size.Width,
                }
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