using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

public class ProgressBar() : Control("ProgressBar")
{
    public int Value
    {
        get;
        set => SetProperty(nameof(Value), ref field, value);
    }

    public Color FillColor
    {
        get;
        set => SetProperty(nameof(FillColor), ref field, value);
    } = Color.Green;

    public override IEnumerable<RenderCommand> ToPrimitives(int windowId)
    {
        // Background
        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Type = RenderCommandType.DrawFilledRect,
            Position = Position,
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = BackgroundColor,
                ["Size"] = Size,
            }
        };

        // Fill
        int fillWidth = Size.Width * Math.Clamp(Value, 0, 100) / 100;

        if (fillWidth > 0)
        {
            yield return new RenderCommand
            {
                WindowId = windowId,
                ElementId = PrimitiveId(1),
                Type = RenderCommandType.DrawFilledRect,
                Position = Position,
                Properties = new Dictionary<string, object?>
                {
                    ["Color"] = FillColor,
                    ["Size"] = new Size(fillWidth, Size.Height),
                }
            };
        }

        // Border
        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(2),
            Type = RenderCommandType.DrawRectBorder,
            Position = Position,
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = Color.DarkGray,
                ["Size"] = Size,
            }
        };
    }
}
