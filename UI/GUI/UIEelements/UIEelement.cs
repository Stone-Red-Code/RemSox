namespace RemSox.UI.GUI.UIEelements;

using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using RemSox.Utils;

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