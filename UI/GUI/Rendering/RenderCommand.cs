using System.Drawing;

namespace RemSox.UI.GUI.Rendering;

public class RenderCommand
{
    public required int WindowId { get; set; }
    public required int ElementId { get; set; }
    public required string ElementType { get; set; }
    public required Point Position { get; set; }
    public required IReadOnlyDictionary<string, object?> Properties { get; set; }
}