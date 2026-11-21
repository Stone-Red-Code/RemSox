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
        Assert.Equal(Color.Red, list.First().Properties["Color"]);
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

        Assert.Equal(Color.Black, Assert.Single(list).Properties["Color"]);
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
        RenderCommand sent = Primitive(Label, 1, zIndex: -7);

        RenderCommand received = RenderCommand.FromBytes(sent.ToBytes());

        Assert.Equal(-7, received.ZIndex);
        Assert.Equal(sent.ElementId, received.ElementId);
        Assert.Equal(Color.White.ToArgb(), ((Color)received.Properties["Color"]!).ToArgb());
    }

    [Fact]
    public void WindowCommandsCarryNoZIndexOfTheirOwn()
    {
        RenderCommand move = new() { WindowId = 4, ElementId = 4, Type = RenderCommandType.MoveWindow, Position = new Point(10, 20) };

        RenderCommand received = RenderCommand.FromBytes(move.ToBytes());

        Assert.False(move.IsDrawPrimitive);
        Assert.Equal(new Point(10, 20), received.Position);
    }

    private static int Id(int elementId, int index)
    {
        return PrimitiveList.GetPrimitiveId(elementId, index);
    }

    private static int[] Ids(PrimitiveList list)
    {
        return [.. list.Select(p => p.ElementId)];
    }

    private static RenderCommand Primitive(int elementId, int index, int zIndex = 0, Color? color = null)
    {
        return new RenderCommand
        {
            WindowId = 1,
            ElementId = Id(elementId, index),
            Type = RenderCommandType.DrawFilledRect,
            ZIndex = zIndex,
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = color ?? Color.White,
                ["Size"] = new Size(10, 10),
            },
        };
    }

    private static RenderCommand Frame(int id)
    {
        return new RenderCommand
        {
            WindowId = 1,
            ElementId = id,
            Type = RenderCommandType.DrawFilledRect,
            Properties = new Dictionary<string, object?> { ["Color"] = Color.Gray, ["Size"] = new Size(10, 10) },
        };
    }
}
