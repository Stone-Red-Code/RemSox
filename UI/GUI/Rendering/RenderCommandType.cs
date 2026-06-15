namespace RemSox.UI.GUI.Rendering;

public enum RenderCommandType : byte
{
    CreateWindow = 0x01,
    DestroyWindow = 0x02,
    MoveWindow = 0x03,

    DrawFilledRect = 0x10,
    DrawRectBorder = 0x11,
    DrawFilledCircle = 0x12,
    DrawCircle = 0x13,
    DrawText = 0x14,
    DrawLine = 0x15,
    DrawPoint = 0x16,

    RemovePrimitives = 0x20,
}
