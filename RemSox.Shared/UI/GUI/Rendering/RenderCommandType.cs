namespace RemSox.Shared.UI.GUI.Rendering;

/// <summary>
/// <para>Render command opcodes, grouped by category:</para>
/// <para>
///   0x01–0x0F  System/setup<br/>
///   0x10–0x1F  Window lifecycle<br/>
///   0x20–0x2F  Primitives lifecycle<br/>
///   0x30–0x3F  Primitives draw<br/>
///   0x40+      Future expansion
/// </para>
/// </summary>
public enum RenderCommandType : byte
{
    // System / setup (0x01–0x0F)
    ScreenInfo = 0x01,
    SetCursor = 0x02,

    /// <summary>Sets the pointer's image and hotspot.</summary>
    SetCursorShape = 0x03,

    /// <summary>Sets what is drawn behind the windows: a colour, and an image the size of the screen.</summary>
    SetBackground = 0x04,

    // Window lifecycle (0x10–0x1F)
    CreateWindow = 0x10,
    DestroyWindow = 0x11,
    MoveWindow = 0x12,

    /// <summary>Sets how opaque a window is composited (property "Opacity", 0–255).</summary>
    SetWindowOpacity = 0x13,

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

    /// <summary>Draws an image at its own size (an image viewer's picture).</summary>
    DrawImage = 0x37,

    /// <summary>Fills a rectangle with rounded corners.</summary>
    DrawFilledRoundedRect = 0x38,

    /// <summary>Draws a rectangle's outline with rounded corners.</summary>
    DrawRoundedRectBorder = 0x39,
}
