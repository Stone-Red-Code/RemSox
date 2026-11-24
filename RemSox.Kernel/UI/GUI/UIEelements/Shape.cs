namespace RemSox.Kernel.UI.GUI.UIEelements;

public abstract class Shape(string type) : UIElement(type)
{
    /// <summary>Gets or sets the shape's colour: a fixed one, or a role of the theme (<see cref="ThemeRole.Text"/> by default).</summary>
    public ThemeColor Color
    {
        get;
        set => SetProperty(nameof(Color), ref field, value);
    } = ThemeRole.Text;
}