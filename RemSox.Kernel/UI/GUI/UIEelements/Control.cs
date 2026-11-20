using RemSox.Shared.UI;
using Cosmos.Kernel.System.Input;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements;

public abstract class Control(string type) : UIElement(type)
{
    public Color BackgroundColor
    {
        get;
        set => SetProperty(nameof(BackgroundColor), ref field, value);
    } = Color.LightGray;

    public Size Size
    {
        get;
        set => SetProperty(nameof(Size), ref field, value);
    }

    /// <summary>Gets whether the control gets the window's keys; set by <see cref="Windows.Window.Focus"/>.</summary>
    public bool IsFocused
    {
        get;
        internal set => SetProperty(nameof(IsFocused), ref field, value);
    }

    public virtual void HandleMouseEvent(MouseEvent mouseEvent)
    {
    }

    public virtual void HandleKeyEvent(KeyEvent keyEvent)
    {
    }
}
