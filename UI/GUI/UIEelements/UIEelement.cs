using RemSox.UI.GUI.Rendering;
using RemSox.Utils;

using System.Drawing;

namespace RemSox.UI.GUI.UIEelements;

public abstract class UIElement(string type) : ChangedPropertiesTracker
{
    public const int PrimitiveIdShift = 6;

    public int Id { get; init; }

    public string Type { get; set; } = type;

    public Point Position
    {
        get;
        set => SetProperty(nameof(Position), ref field, value);
    }

    /// <summary> Expands this UI element into drawing primitives. </summary>
    public abstract IEnumerable<RenderCommand> ToPrimitives(int windowId);

    /// <summary> Builds a stable primitive ID from element ID and sub-index. </summary>
    protected int PrimitiveId(int subIndex)
    {
        return (Id << PrimitiveIdShift) | subIndex;
    }
}