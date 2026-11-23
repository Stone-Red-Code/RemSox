using System.Drawing;

using RemSox.Shared.UI.GUI.Rendering;

namespace RemSox.Tests.Shared;

/// <summary>The screen model both renderers keep: windows, their order, and drawing with clip rectangles.</summary>
public sealed class SceneTests
{
    [Fact]
    public void WindowsAreOrderedByLayerThenZIndexThenId()
    {
        Scene scene = new();
        _ = scene.Apply(Create(1, zIndex: 5));
        _ = scene.Apply(Create(2, zIndex: 1));
        _ = scene.Apply(Create(3, zIndex: 0, layer: WindowLayer.Foreground));
        _ = scene.Apply(Create(4, zIndex: 1));

        Assert.Equal([2, 4, 1, 3], scene.Windows.Select(w => w.Id));
    }

    [Fact]
    public void CommandsForAWindowThatIsGoneAreIgnored()
    {
        Scene scene = new();
        _ = scene.Apply(Create(1));
        _ = scene.Apply(new DestroyWindowCommand { WindowId = 1, ElementId = 1 });

        Assert.Equal(SceneChange.None, scene.Apply(Rect(1, 1)));
        Assert.Empty(scene.Windows);
    }

    [Fact]
    public void ApplyingSaysWhatChanged()
    {
        Scene scene = new();

        Assert.Equal(SceneChange.Layout | SceneChange.Content, scene.Apply(Create(1)));
        Assert.Equal(SceneChange.Content, scene.Apply(Rect(1, 1)));
        Assert.Equal(SceneChange.Layout, scene.Apply(new MoveWindowCommand { WindowId = 1, ElementId = 1, Position = new Point(3, 3) }));
        Assert.Equal(SceneChange.Cursor, scene.Apply(new SetCursorCommand { WindowId = 0, ElementId = 0, Position = new Point(1, 1) }));
        Assert.Equal(SceneChange.ScreenSize | SceneChange.Layout, scene.Apply(new ScreenInfoCommand { WindowId = 0, ElementId = 0, Size = new Size(800, 600) }));
        Assert.Equal(SceneChange.None, scene.Apply(new ScreenInfoCommand { WindowId = 0, ElementId = 0, Size = new Size(800, 600) }));
    }

    [Fact]
    public void DrawSetsEachClipOnceAndLiftsItAtTheEnd()
    {
        Scene scene = new();
        _ = scene.Apply(Create(1));
        Rectangle panel = new(10, 10, 50, 50);
        _ = scene.Apply(Rect(1, 1));
        _ = scene.Apply(Rect(1, 2, panel));
        _ = scene.Apply(Rect(1, 3, panel));
        _ = scene.Apply(Rect(1, 4));
        RecordingCanvas canvas = new();

        Scene.Draw(scene.Find(1)!, canvas);

        Assert.Equal(["fill", "clip 10,10,50,50", "fill", "fill", "clip none", "fill"], canvas.Calls);
        Assert.False(scene.Find(1)!.IsDirty);
    }

    private static CreateWindowCommand Create(int id, int zIndex = 0, WindowLayer layer = WindowLayer.Normal)
    {
        return new CreateWindowCommand { WindowId = id, ElementId = id, Size = new Size(100, 100), ZIndex = zIndex, Layer = layer };
    }

    private static FilledRectangle Rect(int windowId, int elementId, Rectangle? clip = null)
    {
        return new FilledRectangle { WindowId = windowId, ElementId = PrimitiveList.GetPrimitiveId(elementId, 0), Size = new Size(5, 5), Clip = clip };
    }

    private sealed class RecordingCanvas : IPrimitiveCanvas
    {
        public List<string> Calls { get; } = [];

        public void FillRectangle(Color color, Rectangle bounds) => Calls.Add("fill");

        public void DrawRectangle(Color color, Rectangle bounds) => Calls.Add("rect");

        public void FillCircle(Color color, Point topLeft, int radius) => Calls.Add("fill circle");

        public void DrawCircle(Color color, Point topLeft, int radius) => Calls.Add("circle");

        public void DrawLine(Color color, Point start, Point end) => Calls.Add("line");

        public void DrawPoint(Color color, Point point) => Calls.Add("point");

        public void DrawText(string text, Color color, Point position, int fontSize, int maxWidth) => Calls.Add("text");

        public void SetClip(Rectangle? clip) => Calls.Add(clip is { } c ? $"clip {c.X},{c.Y},{c.Width},{c.Height}" : "clip none");
    }
}
