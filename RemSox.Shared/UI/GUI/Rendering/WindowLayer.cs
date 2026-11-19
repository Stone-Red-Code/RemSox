namespace RemSox.Shared.UI.GUI.Rendering;

/// <summary>
/// The stacking layer of a window: every window of a higher layer is drawn above every window of
/// a lower one; the Z-index orders windows within a layer.
/// </summary>
public enum WindowLayer : byte
{
    Background,
    Normal,
    Foreground
}
