using RemSox.Shared.Imaging;
using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Shapes;

/// <summary>
/// An image shown at its own size: scale it to the size it should take first
/// (<see cref="PixelImage.ScaleToFit"/>). It is sent again only when it is replaced or moved, so
/// a large one costs nothing while the rest of its window changes.
/// </summary>
public sealed class Picture() : UIElement("Picture")
{
    /// <summary>Gets or sets the image, opaque (<see cref="PixelImage.Flatten"/>), or null to show nothing.</summary>
    public PixelImage? Image
    {
        get;
        set => SetProperty(nameof(Image), ref field, value);
    }

    public override Size Extent => Image is { } image ? new Size(image.Width, image.Height) : Size.Empty;

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        if (Image is { } image)
        {
            yield return new ImagePrimitive { WindowId = windowId, ElementId = PrimitiveId(0), Position = Position, Image = image };
        }
    }
}
