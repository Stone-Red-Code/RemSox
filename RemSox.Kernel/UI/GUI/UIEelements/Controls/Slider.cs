using RemSox.Shared.UI;
using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

public class Slider() : Control("Slider")
{
    public event EventHandler? OnValueChanged;

    public int MinValue
    {
        get;
        set => SetProperty(nameof(MinValue), ref field, value);
    }

    public int MaxValue
    {
        get;
        set => SetProperty(nameof(MaxValue), ref field, value);
    } = 100;

    public int Value
    {
        get;
        set
        {
            int clamped = Math.Clamp(value, MinValue, MaxValue);
            SetProperty(nameof(Value), ref field, clamped);
            if (Value == clamped)
            {
                OnValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public Color ThumbColor
    {
        get;
        set => SetProperty(nameof(ThumbColor), ref field, value);
    } = Color.LightGray;

    public override IEnumerable<RenderCommand> ToPrimitives(int windowId)
    {
        int trackHeight = 4;
        int trackY = Position.Y + (Size.Height / 2) - (trackHeight / 2);

        // Track background
        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Type = RenderCommandType.DrawFilledRect,
            Position = new Point(Position.X, trackY),
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = Color.DimGray,
                ["Size"] = new Size(Size.Width, trackHeight),
            }
        };

        // Filled portion
        int range = MaxValue - MinValue;
        int fillWidth = range > 0 ? Size.Width * (Value - MinValue) / range : 0;

        if (fillWidth > 0)
        {
            yield return new RenderCommand
            {
                WindowId = windowId,
                ElementId = PrimitiveId(1),
                Type = RenderCommandType.DrawFilledRect,
                Position = new Point(Position.X, trackY),
                Properties = new Dictionary<string, object?>
                {
                    ["Color"] = BackgroundColor,
                    ["Size"] = new Size(fillWidth, trackHeight),
                }
            };
        }

        // Thumb (centered at the fill edge)
        int thumbSize = Size.Height;
        int thumbX = fillWidth - (thumbSize / 2);
        int thumbY = Position.Y;

        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(2),
            Type = RenderCommandType.DrawFilledCircle,
            Position = new Point(Position.X + thumbX, thumbY),
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = ThumbColor,
                ["Radius"] = thumbSize / 2,
            }
        };

        // Thumb border
        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(3),
            Type = RenderCommandType.DrawCircle,
            Position = new Point(Position.X + thumbX, thumbY),
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = Color.DarkGray,
                ["Radius"] = thumbSize / 2,
            }
        };
    }

    private bool isDragging;

    public override void HandleMouseEvent(MouseEvent mouseEvent)
    {
        if (mouseEvent.Type == MouseEventType.ButtonDown && mouseEvent.Button == MouseButton.Left)
        {
            isDragging = true;
            SetValueFromX(mouseEvent.X);
        }
        else if (mouseEvent.Type == MouseEventType.Move && isDragging)
        {
            SetValueFromX(mouseEvent.X);
        }
        else if (mouseEvent.Type == MouseEventType.ButtonUp && mouseEvent.Button == MouseButton.Left)
        {
            isDragging = false;
        }
    }

    private void SetValueFromX(int windowX)
    {
        int localX = windowX - Position.X;
        int range = MaxValue - MinValue;
        Value = range > 0
            ? MinValue + (localX * range / Size.Width)
            : MinValue;
    }
}
