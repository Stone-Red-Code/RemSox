using RemSox.Shared.UI.GUI.Rendering;
using RemSox.Kernel.Utils;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements;

public abstract class UIElement(string type) : ChangedPropertiesTracker
{
    public int Id { get; init; }

    public string Type { get; set; } = type;

    public Point Position
    {
        get;
        set => SetProperty(nameof(Position), ref field, value);
    }

    /// <summary>
    /// Where the element is drawn among the others of its window: a higher z-index over a lower
    /// one. Elements with the same z-index (by default all, at 0) are drawn in the order they
    /// were created, so a background created before its text stays under it. Set it only for an
    /// element that must cover ones created after it, such as a popup made in advance.
    /// </summary>
    /// <remarks>
    /// It orders an element among its siblings: today every element is a direct child of its
    /// window, but an element inside a container will be ordered within that container only.
    /// </remarks>
    public int ZIndex
    {
        get;
        set => SetProperty(nameof(ZIndex), ref field, value);
    }

    /// <summary>
    /// Gets or sets the rectangle of the window outside which nothing of the element is drawn
    /// or clicked, or null for none. A <see cref="Controls.ScrollPanel"/> sets it on what it
    /// holds, so what is scrolled out of view is hidden.
    /// </summary>
    public Rectangle? ClipBounds
    {
        get;
        set => SetProperty(nameof(ClipBounds), ref field, value);
    }

    /// <summary>
    /// Gets the size the element takes from its <see cref="Position"/>, so a container can tell
    /// how much content it holds; empty for an element that does not say.
    /// </summary>
    public virtual Size Extent => Size.Empty;

    /// <summary>Gets the window the element is in; set when the window creates it.</summary>
    public Windows.Window? Window { get; internal set; }

    /// <summary> Expands this UI element into drawing primitives. </summary>
    public abstract IEnumerable<Primitive> ToPrimitives(int windowId);

    /// <summary> Builds a stable primitive ID from element ID and sub-index. </summary>
    protected int PrimitiveId(int subIndex)
    {
        return PrimitiveList.GetPrimitiveId(Id, subIndex);
    }
}