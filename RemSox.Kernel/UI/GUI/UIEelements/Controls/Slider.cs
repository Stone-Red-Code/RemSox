using RemSox.Shared.UI;
using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

public sealed class Slider() : Control("Slider", ThemeRole.Accent)
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
            if (clamped != field)
            {
                SetProperty(nameof(Value), ref field, clamped);
                OnValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public ThemeColor ThumbColor
    {
        get;
        set => SetProperty(nameof(ThumbColor), ref field, value);
    } = ThemeRole.Text;

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        int trackHeight = 4;
        int trackY = Position.Y + (Size.Height / 2) - (trackHeight / 2);

        // Track background
        yield return new FilledRoundedRectangle
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Position = new Point(Position.X, trackY),
            Color = Theme[ThemeRole.Control],
            Size = new Size(Size.Width, trackHeight),
            Radius = trackHeight / 2,
        };

        // Filled portion
        int range = MaxValue - MinValue;
        int fillWidth = range > 0 ? Size.Width * (Value - MinValue) / range : 0;

        if (fillWidth > 0)
        {
            yield return new FilledRoundedRectangle
            {
                WindowId = windowId,
                ElementId = PrimitiveId(1),
                Position = new Point(Position.X, trackY),
                Color = Resolve(BackgroundColor),
                Size = new Size(fillWidth, trackHeight),
                Radius = trackHeight / 2,
            };
        }

        // Thumb (centered at the fill edge)
        int thumbSize = Size.Height;
        int thumbX = fillWidth - (thumbSize / 2);
        int thumbY = Position.Y;

        yield return new FilledCircle
        {
            WindowId = windowId,
            ElementId = PrimitiveId(2),
            Position = new Point(Position.X + thumbX, thumbY),
            Color = Resolve(ThumbColor),
            Radius = thumbSize / 2,
        };

        // Thumb border
        yield return new CircleOutline
        {
            WindowId = windowId,
            ElementId = PrimitiveId(3),
            Position = new Point(Position.X + thumbX, thumbY),
            Color = Theme[ThemeRole.Border],
            Radius = thumbSize / 2,
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
