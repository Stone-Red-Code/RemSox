using System.Drawing;

using RemSox.Shared.Imaging;

namespace RemSox.Shared.UI.GUI.Rendering;

/// <summary>What applying a render command to a <see cref="Scene"/> changed, so a renderer knows what to redo.</summary>
[Flags]
public enum SceneChange
{
    None = 0,

    /// <summary>The screen's size.</summary>
    ScreenSize = 1,

    /// <summary>The pointer's position.</summary>
    Cursor = 2,

    /// <summary>The pointer's image: the cursor must be drawn again, the scene under it need not.</summary>
    CursorShape = 4,

    /// <summary>Which windows there are, where, how big, how stacked or how opaque: the screen must be composited again.</summary>
    Layout = 8,

    /// <summary>A window's primitives: it must be drawn again (<see cref="SceneWindow.IsDirty"/>).</summary>
    Content = 16,

    /// <summary>What is behind the windows: the screen must be composited again, from a new background.</summary>
    Background = 32,
}

/// <summary>One window of a <see cref="Scene"/>.</summary>
public sealed class SceneWindow(int id)
{
    public int Id { get; } = id;

    public Point Position { get; internal set; }

    public Size Size { get; internal set; }

    public int ZIndex { get; internal set; }

    public WindowLayer Layer { get; internal set; } = WindowLayer.Normal;

    public byte Opacity { get; internal set; } = 255;

    /// <summary>Gets the radius of its rounded corners, already fitted to its size; 0 for square ones.</summary>
    public int CornerRadius { get; internal set; }

    /// <summary>Gets the window's primitives, in the order they are drawn.</summary>
    public PrimitiveList Primitives { get; } = [];

    /// <summary>Gets whether its primitives changed since it was last drawn; <see cref="Scene.Draw"/> clears it.</summary>
    public bool IsDirty { get; internal set; } = true;

    /// <summary>Marks the window to be drawn again, as when its renderer lost what it had drawn (a new canvas).</summary>
    public void Invalidate()
    {
        IsDirty = true;
    }
}

/// <summary>
/// The screen as render commands describe it: its size, the pointer, and the windows with their
/// primitives, kept the same way by both renderers (the kernel's canvas and the viewer), so they
/// cannot disagree on what is where. A renderer applies each command it gets, then composites
/// <see cref="Windows"/> in order, drawing a window with <see cref="Draw"/> when it is dirty.
/// </summary>
public sealed class Scene
{
    private readonly Dictionary<int, SceneWindow> windows = [];
    private List<SceneWindow>? ordered;

    public Size ScreenSize { get; private set; } = new(640, 480);

    public Point CursorPosition { get; private set; }

    /// <summary>Gets the pointer's image, or null before the screen sent one: nothing is drawn for the pointer then.</summary>
    public CursorShape? CursorShape { get; private set; }

    /// <summary>Gets the colour behind the windows where there is no image.</summary>
    public Color BackgroundColor { get; private set; } = Color.FromArgb(45, 45, 48);

    /// <summary>Gets the image behind the windows, the size of the screen, or null for the colour alone.</summary>
    public PixelImage? BackgroundImage { get; private set; }

    /// <summary>Gets the windows from the bottom up: by layer, then z-index, then id (the order they were made in).</summary>
    public IReadOnlyList<SceneWindow> Windows => ordered ??= [.. windows.Values.OrderBy(w => w.Layer).ThenBy(w => w.ZIndex).ThenBy(w => w.Id)];

    /// <summary>Gets whether any window must be drawn again.</summary>
    public bool AnyDirty => windows.Values.Any(w => w.IsDirty);

    public SceneWindow? Find(int windowId)
    {
        return windows.GetValueOrDefault(windowId);
    }

    /// <summary>
    /// Applies one command. Commands about a window that does not exist (any more) are ignored,
    /// as they may arrive after it was destroyed.
    /// </summary>
    public SceneChange Apply(RenderCommand command)
    {
        switch (command)
        {
            case ScreenInfoCommand info:
                if (info.Size == ScreenSize)
                {
                    return SceneChange.None;
                }

                ScreenSize = info.Size;
                return SceneChange.ScreenSize | SceneChange.Layout;

            case SetCursorCommand cursor:
                CursorPosition = cursor.Position;
                return SceneChange.Cursor;

            case SetCursorShapeCommand shape:
                CursorShape = shape.Shape;
                return SceneChange.CursorShape;

            case SetBackgroundCommand background:
                BackgroundColor = background.Color;
                BackgroundImage = background.Image;
                return SceneChange.Background | SceneChange.Layout;

            case CreateWindowCommand create:
                if (!windows.TryGetValue(create.WindowId, out SceneWindow? window))
                {
                    window = new SceneWindow(create.WindowId);
                    windows[create.WindowId] = window;
                }

                window.Position = create.Position;
                window.Size = create.Size;
                window.ZIndex = create.ZIndex;
                window.Layer = create.Layer;
                window.CornerRadius = RoundedCorners.Clamp(create.CornerRadius, create.Size.Width, create.Size.Height);
                window.IsDirty = true;
                ordered = null;
                return SceneChange.Layout | SceneChange.Content;

            case DestroyWindowCommand:
                if (!windows.Remove(command.WindowId))
                {
                    return SceneChange.None;
                }

                ordered = null;
                return SceneChange.Layout;

            case MoveWindowCommand move when windows.TryGetValue(move.WindowId, out SceneWindow? moved):
                moved.Position = move.Position;
                return SceneChange.Layout;

            case SetWindowOpacityCommand fade when windows.TryGetValue(fade.WindowId, out SceneWindow? faded):
                faded.Opacity = fade.Opacity;
                return SceneChange.Layout;

            case RemovePrimitivesCommand when windows.TryGetValue(command.WindowId, out SceneWindow? target):
                target.Primitives.RemoveElement(command.ElementId);
                target.IsDirty = true;
                return SceneChange.Content;

            case Primitive primitive when windows.TryGetValue(primitive.WindowId, out SceneWindow? drawn):
                drawn.Primitives.Set(primitive);
                drawn.IsDirty = true;
                return SceneChange.Content;

            default:
                return SceneChange.None;
        }
    }

    /// <summary>Forgets every window, for showing another screen or a fresh start after reconnecting.</summary>
    public void Clear()
    {
        windows.Clear();
        ordered = null;
    }

    /// <summary>
    /// Draws a window's primitives in order on <paramref name="canvas"/> (cleared by the caller),
    /// limiting each to its clip rectangle, and marks the window drawn.
    /// </summary>
    public static void Draw(SceneWindow window, IPrimitiveCanvas canvas)
    {
        Rectangle? clip = null;
        foreach (Primitive primitive in window.Primitives)
        {
            if (primitive.Clip != clip)
            {
                clip = primitive.Clip;
                canvas.SetClip(clip);
            }

            primitive.Draw(canvas);
        }

        if (clip is not null)
        {
            canvas.SetClip(null);
        }

        window.IsDirty = false;
    }
}
