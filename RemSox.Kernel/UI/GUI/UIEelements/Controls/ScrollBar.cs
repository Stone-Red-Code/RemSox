using RemSox.Shared.UI;
using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

/// <summary>
/// A vertical scroll bar: a track with a thumb whose length shows how much of the content is in
/// view and whose place shows which part. Dragging the thumb scrolls to that place; clicking the
/// track above or below it scrolls by a page.
/// </summary>
/// <remarks>
/// It only shows and reports a position; the owner scrolls its content. Lengths are in whatever
/// unit the content uses (rows, pixels). Setting <see cref="Value"/> from code does not raise
/// <see cref="Scrolled"/>, so an owner can keep the bar in step with its own scrolling (keys, the
/// wheel) without a feedback loop. Nothing is drawn, and clicks pass through, while the content
/// fits in view or <see cref="Control.Size"/> is empty.
/// </remarks>
public sealed class ScrollBar() : Control("ScrollBar")
{
    private const int MinThumbLength = 12;

    // While dragging: how far below the thumb's top the pointer grabbed it.
    private int? dragOffset;

    /// <summary>Raised with the new <see cref="Value"/> when the user moves the bar.</summary>
    public event Action<int>? Scrolled;

    /// <summary>Gets or sets the length of the whole content.</summary>
    public int Maximum
    {
        get;
        set
        {
            SetProperty(nameof(Maximum), ref field, Math.Max(0, value));
            Value = Value;
        }
    }

    /// <summary>Gets or sets how much of the content is in view at once; also what a click on the track scrolls.</summary>
    public int ViewportSize
    {
        get;
        set
        {
            SetProperty(nameof(ViewportSize), ref field, Math.Max(1, value));
            Value = Value;
        }
    } = 1;

    /// <summary>Gets or sets where the view starts: 0 at the top, up to <see cref="Maximum"/> minus <see cref="ViewportSize"/>.</summary>
    public int Value
    {
        get;
        set => SetProperty(nameof(Value), ref field, Math.Clamp(value, 0, MaxValue));
    }

    public Color ThumbColor
    {
        get;
        set => SetProperty(nameof(ThumbColor), ref field, value);
    } = Color.FromArgb(120, 120, 120);

    /// <summary>Gets whether the user is dragging the thumb (it is drawn lighter then).</summary>
    public bool IsDragging
    {
        get;
        private set => SetProperty(nameof(IsDragging), ref field, value);
    }

    /// <summary>Gets whether there is anything to scroll, and so whether the bar shows.</summary>
    public bool IsActive => Maximum > ViewportSize && Size.Width > 0 && Size.Height > 0;

    private int MaxValue => Math.Max(0, Maximum - ViewportSize);

    private int ThumbLength => Math.Min(Size.Height, Math.Max(MinThumbLength, Size.Height * ViewportSize / Math.Max(1, Maximum)));

    private int ThumbTravel => Size.Height - ThumbLength;

    private int ThumbTop => MaxValue > 0 ? ThumbTravel * Value / MaxValue : 0;

    public override IEnumerable<RenderCommand> ToPrimitives(int windowId)
    {
        if (!IsActive)
        {
            yield break;
        }

        // Track
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

        // Thumb, lighter while dragged
        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(1),
            Type = RenderCommandType.DrawFilledRect,
            Position = new Point(Position.X + 1, Position.Y + ThumbTop),
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = IsDragging ? Color.FromArgb(170, 170, 170) : ThumbColor,
                ["Size"] = new Size(Math.Max(1, Size.Width - 2), ThumbLength),
            }
        };
    }

    public override void HandleMouseEvent(MouseEvent mouseEvent)
    {
        if (!IsActive)
        {
            return;
        }

        int y = mouseEvent.Y - Position.Y;

        switch (mouseEvent.Type)
        {
            case MouseEventType.ButtonDown when mouseEvent.Button == MouseButton.Left:
                int thumbTop = ThumbTop;
                if (y < thumbTop)
                {
                    ScrollTo(Value - ViewportSize);
                }
                else if (y >= thumbTop + ThumbLength)
                {
                    ScrollTo(Value + ViewportSize);
                }
                else
                {
                    dragOffset = y - thumbTop;
                    IsDragging = true;
                }
                break;

            case MouseEventType.Move when dragOffset is { } offset:
                // The value whose thumb top is nearest the pointer, less where it was grabbed.
                int travel = ThumbTravel;
                if (travel > 0)
                {
                    int top = Math.Clamp(y - offset, 0, travel);
                    ScrollTo(((top * MaxValue) + (travel / 2)) / travel);
                }
                break;

            case MouseEventType.ButtonUp when mouseEvent.Button == MouseButton.Left:
                dragOffset = null;
                IsDragging = false;
                break;
        }
    }

    private void ScrollTo(int value)
    {
        int before = Value;
        Value = value;

        if (Value != before)
        {
            Scrolled?.Invoke(Value);
        }
    }
}
