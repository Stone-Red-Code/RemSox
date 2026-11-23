using RemSox.Shared.UI;
using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

/// <summary>
/// A box holding more than fits in it: its elements are placed in content coordinates (0, 0 at
/// the panel's top left) and scrolled up and down by the wheel or the scroll bar it shows at its
/// right while the content is taller than the panel. What is scrolled out of view is neither
/// drawn nor clicked (each element's <see cref="UIElement.ClipBounds"/>).
/// </summary>
/// <remarks>
/// <para>
/// Create the panel in a window first, then its elements with <see cref="Add{T}"/>: they are
/// ordinary elements of the window that the panel moves as it scrolls, so they draw, focus and
/// take clicks like any other. Move one later with <see cref="MoveChild"/>, since setting its
/// <see cref="UIElement.Position"/> directly is overwritten at the next scroll.
/// </para>
/// <para>
/// Scrolling moves every element, which sends each one again: fine for the tens of rows a
/// settings page or a short list has. A list of thousands should show only its visible rows,
/// as Explorer does.
/// </para>
/// </remarks>
public sealed class ScrollPanel : Control
{
    /// <summary>Width of the scroll bar, taken from the content's width while it shows.</summary>
    public const int ScrollBarWidth = 10;

    private readonly Dictionary<UIElement, Point> children = [];
    private ScrollBar? scrollBar;
    private bool listening;

    public ScrollPanel() : base("ScrollPanel")
    {
        BackgroundColor = Color.FromArgb(28, 28, 28);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Position) or nameof(Size) or nameof(ClipBounds) or nameof(ZIndex))
            {
                Layout();
            }
        };
    }

    /// <summary>Gets how far the content is scrolled, in pixels from its top.</summary>
    public int ScrollOffset { get; private set; }

    /// <summary>Gets or sets how many pixels one notch of the wheel scrolls.</summary>
    public int WheelStep { get; set; } = 48;

    /// <summary>Gets how tall the content is: down to the bottom of its lowest element.</summary>
    public int ContentHeight => children.Count == 0 ? 0 : children.Max(child => child.Value.Y + child.Key.Extent.Height);

    /// <summary>Gets the part of the window the content shows in: the panel less its scroll bar while it shows.</summary>
    public Rectangle ContentBounds
    {
        get
        {
            int width = Size.Width - (ContentHeight > Size.Height ? ScrollBarWidth : 0);
            Rectangle bounds = new(Position, new Size(Math.Max(0, width), Size.Height));
            return ClipBounds is { } outer ? Rectangle.Intersect(bounds, outer) : bounds;
        }
    }

    /// <summary>Gets the elements the panel holds, with where each is in the content.</summary>
    public IReadOnlyDictionary<UIElement, Point> Children => children;

    /// <summary>
    /// Creates an element in the panel's window and puts it in the panel; its
    /// <see cref="UIElement.Position"/>, as <paramref name="options"/> sets it, is where it goes in
    /// the content.
    /// </summary>
    /// <exception cref="InvalidOperationException">The panel is not in a window yet.</exception>
    public T Add<T>(Action<T>? options = null) where T : UIElement, new()
    {
        Windows.Window window = Window ?? throw new InvalidOperationException("Create the scroll panel in a window before adding to it.");
        Listen(window);

        T child = window.CreateUIElement<T>(element =>
        {
            options?.Invoke(element);

            // Placed before the window first draws it, so it never shows outside the panel.
            children[element] = element.Position;
            element.ZIndex = ZIndex;
            Place(element, element.Position);
        });

        Layout();
        return child;
    }

    /// <summary>Takes an element out of the panel and its window.</summary>
    public void Remove(UIElement child)
    {
        if (children.Remove(child))
        {
            Window?.RemoveUIElement(child.Id);
            Layout();
        }
    }

    /// <summary>Takes every element out of the panel and its window, and scrolls back to the top.</summary>
    public void Clear()
    {
        foreach (UIElement child in children.Keys.ToList())
        {
            _ = children.Remove(child);
            Window?.RemoveUIElement(child.Id);
        }

        ScrollOffset = 0;
        Layout();
    }

    /// <summary>Moves an element of the panel to <paramref name="contentPosition"/> in the content.</summary>
    public void MoveChild(UIElement child, Point contentPosition)
    {
        if (children.ContainsKey(child))
        {
            children[child] = contentPosition;
            Layout();
        }
    }

    /// <summary>Scrolls so the content starts <paramref name="offset"/> pixels above the panel's top (kept in range).</summary>
    public void ScrollTo(int offset)
    {
        int scrolled = Math.Clamp(offset, 0, Math.Max(0, ContentHeight - Size.Height));
        if (scrolled != ScrollOffset)
        {
            ScrollOffset = scrolled;
            Layout();
            Window?.Flush();
        }
    }

    /// <summary>Scrolls just enough to show the whole of <paramref name="child"/>, or its top when it is taller than the panel.</summary>
    public void ScrollIntoView(UIElement child)
    {
        if (!children.TryGetValue(child, out Point position))
        {
            return;
        }

        int bottom = position.Y + child.Extent.Height;
        if (position.Y < ScrollOffset || child.Extent.Height > Size.Height)
        {
            ScrollTo(position.Y);
        }
        else if (bottom > ScrollOffset + Size.Height)
        {
            ScrollTo(bottom - Size.Height);
        }
    }

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        yield return new FilledRectangle
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Position = Position,
            Color = BackgroundColor,
            Size = Size,
        };
    }

    /// <summary>Puts every element where the scroll offset shows it, clipped to the content, and sets up the bar.</summary>
    private void Layout()
    {
        if (Window is null)
        {
            return;
        }

        int maxOffset = Math.Max(0, ContentHeight - Size.Height);
        ScrollOffset = Math.Clamp(ScrollOffset, 0, maxOffset);

        foreach ((UIElement child, Point position) in children)
        {
            child.ZIndex = ZIndex;
            Place(child, position);
        }

        if (scrollBar is not null)
        {
            scrollBar.ZIndex = ZIndex + 1;
            scrollBar.ClipBounds = ClipBounds;
            scrollBar.Position = new Point(Position.X + Size.Width - ScrollBarWidth, Position.Y);
            scrollBar.Size = new Size(ScrollBarWidth, Size.Height);
            scrollBar.Maximum = ContentHeight;
            scrollBar.ViewportSize = Math.Max(1, Size.Height);
            scrollBar.Value = ScrollOffset;
        }
    }

    private void Place(UIElement child, Point contentPosition)
    {
        child.Position = new Point(Position.X + contentPosition.X, Position.Y + contentPosition.Y - ScrollOffset);
        child.ClipBounds = ContentBounds;
    }

    /// <summary>Makes the scroll bar and listens to the wheel, once the panel's window is known.</summary>
    private void Listen(Windows.Window window)
    {
        if (listening)
        {
            return;
        }

        listening = true;
        scrollBar = window.CreateUIElement<ScrollBar>(bar => bar.BackgroundColor = Color.FromArgb(48, 48, 48));
        scrollBar.Scrolled += ScrollTo;

        window.OnMouseEvent += mouseEvent =>
        {
            if (mouseEvent.Type != MouseEventType.Wheel)
            {
                return;
            }

            Point local = new(mouseEvent.X - window.Position.X, mouseEvent.Y - window.Position.Y);
            Rectangle visible = ClipBounds is { } outer ? Rectangle.Intersect(new Rectangle(Position, Size), outer) : new Rectangle(Position, Size);
            if (visible.Contains(local))
            {
                ScrollTo(ScrollOffset + (mouseEvent.Delta * WheelStep));
            }
        };
    }
}
