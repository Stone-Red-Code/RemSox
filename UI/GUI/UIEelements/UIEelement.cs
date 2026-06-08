using RemSox.Utils;

using System.Drawing;

namespace RemSox.UI.GUI.UIEelements;

public abstract class UIElement(string type) : ChangedPropertiesTracker
{
    public int Id { get; init; }

    public string Type { get; set; } = type;

    public Point Position
    {
        get;
        set => SetProperty(nameof(Position), ref field, value);
    }
}