using System.Drawing;

namespace RemSox.UI.GUI.UIEelements.Controls;

public class Button() : Control("Button")
{
    public event EventHandler? OnClick;

    public string Text
    {
        get;
        set => SetProperty(nameof(Text), ref field, value);
    } = string.Empty;

    public Color TextColor
    {
        get;
        set => SetProperty(nameof(TextColor), ref field, value);
    } = Color.Black;

    public override void HandleMouseEvent(MouseEvent mouseEvent)
    {
        if (mouseEvent.Type == MouseEventType.ButtonDown && mouseEvent.Button == MouseButton.Left)
        {
            OnClick?.Invoke(this, EventArgs.Empty);
        }
    }
}