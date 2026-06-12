using System.Drawing;

namespace RemSox.UI.GUI.UIEelements.Controls;

public class CheckBox() : Control("CheckBox")
{
    public event EventHandler? OnCheckedChanged;

    public bool IsChecked
    {
        get;
        set
        {
            SetProperty(nameof(IsChecked), ref field, value);
            OnCheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    } = false;

    public string Text
    {
        get;
        set => SetProperty(nameof(Text), ref field, value);
    } = string.Empty;

    public Color TextColor
    {
        get;
        set => SetProperty(nameof(TextColor), ref field, value);
    } = Color.White;
}