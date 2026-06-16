namespace RemSox.UI.GUI.Rendering;

/// <summary>
/// Render command opcodes, grouped by category:
///   0x01–0x0F  System/setup
///   0x10–0x1F  Window lifecycle
///   0x20–0x2F  Primitives lifecycle
///   0x30–0x3F  Primitives draw
///   0x40+      Future expansion
/// </summary>
public enum RenderCommandType : byte
{
    // System / setup (0x01–0x0F)
    ScreenInfo = 0x01,
    SetCursor = 0x02,

    // Window lifecycle (0x10–0x1F)
    CreateWindow = 0x10,
    DestroyWindow = 0x11,
    MoveWindow = 0x12,

    // Primitives lifecycle (0x20–0x2F)
    RemovePrimitives = 0x20,

    // Primitives draw (0x30–0x3F)
    DrawFilledRect = 0x30,
    DrawRectBorder = 0x31,
    DrawFilledCircle = 0x32,
    DrawCircle = 0x33,
    DrawText = 0x34,
    DrawLine = 0x35,
    DrawPoint = 0x36,
}
