using System.Drawing;

using RemSox.Shared.UI.GUI.Rendering;

namespace RemSox.Tests.Shared;

/// <summary>
/// The draw order both renderers keep per window: the frame, then elements by z-index and
/// creation order, however the primitives arrive.
/// </summary>
public sealed class PrimitiveListTests
{
    private const int Background = 1;
    private const int Label = 2;
    private const int Popup = 3;

    [Fact]
    public void ElementsAreDrawnInTheOrderTheyWereCreated()
    {
        PrimitiveList list = [];

        list.Set(Primitive(Label, 0));
        list.Set(Primitive(Background, 0));

        Assert.Equal([Id(Background, 0), Id(Label, 0)], Ids(list));
    }

    [Fact]
    public void ChangedElementStaysWhereItWas()
    {
        // What Window.Flush sends for a background that changed under its label.
        PrimitiveList list = [];
        list.Set(Primitive(Background, 0));
        list.Set(Primitive(Label, 0));

        list.RemoveElement(Background);
        list.Set(Primitive(Background, 0, color: Color.Red));

        Assert.Equal([Id(Background, 0), Id(Label, 0)], Ids(list));
        Assert.Equal(Color.Red, list.First().Color);
    }

    [Fact]
    public void ElementsPrimitivesKeepTheirOwnOrder()
    {
        PrimitiveList list = [];

        list.Set(Primitive(Background, 2));
        list.Set(Primitive(Background, 0));
        list.Set(Primitive(Background, 1));

        Assert.Equal([Id(Background, 0), Id(Background, 1), Id(Background, 2)], Ids(list));
    }

    [Fact]
    public void HigherZIndexIsDrawnOverElementsCreatedLater()
    {
        // A popup made in advance (the first element), then a label and a background after it.
        const int popupMadeFirst = 1;
        PrimitiveList list = [];

        list.Set(Primitive(popupMadeFirst, 0, zIndex: 1));
        list.Set(Primitive(Label, 0));
        list.Set(Primitive(Popup, 0));

        Assert.Equal([Id(Label, 0), Id(Popup, 0), Id(popupMadeFirst, 0)], Ids(list));
    }

    [Fact]
    public void NegativeZIndexGoesUnderElementsCreatedBefore()
    {
        PrimitiveList list = [];

        list.Set(Primitive(Background, 0));
        list.Set(Primitive(Label, 0, zIndex: -1));

        Assert.Equal([Id(Label, 0), Id(Background, 0)], Ids(list));
    }

    [Fact]
    public void ChangingTheZIndexMovesTheElement()
    {
        PrimitiveList list = [];
        list.Set(Primitive(Background, 0));
        list.Set(Primitive(Label, 0));

        list.RemoveElement(Background);
        list.Set(Primitive(Background, 0, zIndex: 5));

        Assert.Equal([Id(Label, 0), Id(Background, 0)], Ids(list));
    }

    [Fact]
    public void FrameIsDrawnFirstInItsOwnOrderWhateverTheZIndex()
    {
        PrimitiveList list = [];

        list.Set(Primitive(Background, 0, zIndex: int.MinValue));
        list.Set(Frame(-1));
        list.Set(Frame(-13));

        Assert.Equal([-13, -1, Id(Background, 0)], Ids(list));
    }

    [Fact]
    public void SettingTheSamePrimitiveAgainReplacesIt()
    {
        PrimitiveList list = [];

        list.Set(Primitive(Label, 0, color: Color.White));
        list.Set(Primitive(Label, 0, color: Color.Black));

        Assert.Equal(Color.Black, Assert.Single(list).Color);
    }

    [Fact]
    public void RemovingAnElementLeavesTheOthers()
    {
        PrimitiveList list = [];
        list.Set(Primitive(Background, 0));
        list.Set(Primitive(Background, 1));
        list.Set(Primitive(Label, 0));
        list.Set(Frame(-1));

        list.RemoveElement(Background);

        Assert.Equal([-1, Id(Label, 0)], Ids(list));
    }

    [Fact]
    public void RemovingAFramePartRemovesOnlyThatPart()
    {
        PrimitiveList list = [];
        list.Set(Frame(-2));
        list.Set(Frame(-1));

        list.RemoveElement(-1);

        Assert.Equal([-2], Ids(list));
    }

    [Fact]
    public void ZIndexTravelsWithAPrimitive()
    {
        Primitive sent = Primitive(Label, 1, zIndex: -7);

        Primitive received = Assert.IsAssignableFrom<Primitive>(RenderCommand.FromBytes(sent.ToBytes()));

        Assert.Equal(-7, received.ZIndex);
        Assert.Equal(sent.ElementId, received.ElementId);
        Assert.Null(received.Clip);
        Assert.Equal(Color.White.ToArgb(), received.Color.ToArgb());
    }

    [Fact]
    public void EveryKindOfPrimitiveSurvivesTheWire()
    {
        Primitive[] sent =
        [
            new FilledRectangle { WindowId = 2, ElementId = 3, Position = new Point(-4, 5), Color = Color.Red, Size = new Size(6, 7) },
            new RectangleBorder { WindowId = 2, ElementId = 4, Position = new Point(1, 2), Color = Color.Blue, Size = new Size(30, 40), Clip = new Rectangle(1, 2, 3, 4) },
            new FilledCircle { WindowId = 2, ElementId = 5, Position = new Point(8, 9), Color = Color.Lime, Radius = 11 },
            new CircleOutline { WindowId = 2, ElementId = 6, Position = new Point(8, 9), Color = Color.Lime, Radius = 12 },
            new TextPrimitive { WindowId = 2, ElementId = 7, Position = new Point(3, 3), Color = Color.White, Content = "h\u00e9llo", FontSize = 18, MaxWidth = 90, Clip = new Rectangle(-5, 0, 100, 20) },
            new LinePrimitive { WindowId = 2, ElementId = 8, Position = new Point(0, 0), Color = Color.Gray, End = new Point(-10, 20) },
            new PointPrimitive { WindowId = 2, ElementId = 9, Position = new Point(7, 7), Color = Color.Black },
        ];

        foreach (Primitive primitive in sent)
        {
            primitive.ZIndex = 3;
            RenderCommand received = RenderCommand.FromBytes(primitive.ToBytes());

            Assert.IsType(primitive.GetType(), received);
            Assert.Equal(primitive.ToBytes(), received.ToBytes());
        }
    }

    [Fact]
    public void WindowCommandsSurviveTheWire()
    {
        RenderCommand[] sent =
        [
            new MoveWindowCommand { WindowId = 4, ElementId = 4, Position = new Point(10, 20) },
            new CreateWindowCommand { WindowId = 4, ElementId = 4, Position = new Point(1, 2), Size = new Size(300, 200), ZIndex = 9, Layer = WindowLayer.Foreground },
            new SetWindowOpacityCommand { WindowId = 4, ElementId = 4, Opacity = 190 },
            new ScreenInfoCommand { WindowId = 0, ElementId = 0, Size = new Size(1280, 800) },
            new SetCursorCommand { WindowId = 0, ElementId = 0, Position = new Point(5, 6) },
            new DestroyWindowCommand { WindowId = 4, ElementId = 4 },
            new RemovePrimitivesCommand { WindowId = 4, ElementId = 12 },
        ];

        foreach (RenderCommand command in sent)
        {
            RenderCommand received = RenderCommand.FromBytes(command.ToBytes());

            Assert.IsType(command.GetType(), received);
            Assert.Equal(command.ToBytes(), received.ToBytes());
        }
    }

    [Fact]
    public void CutShortCommandIsRefused()
    {
        byte[] bytes = new TextPrimitive { WindowId = 1, ElementId = 1, Content = "abc" }.ToBytes();

        _ = Assert.Throws<InvalidDataException>(() => RenderCommand.FromBytes(bytes[..^3]));
    }

    private static int Id(int elementId, int index)
    {
        return PrimitiveList.GetPrimitiveId(elementId, index);
    }

    private static int[] Ids(PrimitiveList list)
    {
        return [.. list.Select(p => p.ElementId)];
    }

    private static Primitive Primitive(int elementId, int index, int zIndex = 0, Color? color = null)
    {
        return new FilledRectangle
        {
            WindowId = 1,
            ElementId = Id(elementId, index),
            ZIndex = zIndex,
            Color = color ?? Color.White,
            Size = new Size(10, 10),
        };
    }

    private static Primitive Frame(int id)
    {
        return new FilledRectangle { WindowId = 1, ElementId = id, Color = Color.Gray, Size = new Size(10, 10) };
    }
}
