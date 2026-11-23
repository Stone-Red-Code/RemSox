using System.Collections;

namespace RemSox.Shared.UI.GUI.Rendering;

/// <summary>
/// The drawing primitives of one window, always in the order they are drawn: what both
/// renderers (the kernel's canvas and the viewer) keep per window, so they cannot disagree.
/// </summary>
/// <remarks>
/// <para>
/// A primitive's id is its element's id and its index within that element
/// (<see cref="GetPrimitiveId"/>); the window's own frame uses negative ids. The order is:
/// </para>
/// <list type="number">
/// <item>The frame first, by id (the window numbers its parts in the order they are drawn).</item>
/// <item>Then the elements by <see cref="Primitive.ZIndex"/>, lowest first.</item>
/// <item>Elements with the same z-index by id, which is the order they were created in: ids
/// only grow, so what is created later is drawn over what was created before.</item>
/// <item>An element's own primitives by their index within it.</item>
/// </list>
/// <para>
/// The order comes only from these keys, never from when a primitive arrived, so an element
/// sent again because it changed stays where it was instead of moving over everything else.
/// </para>
/// </remarks>
public sealed class PrimitiveList : IEnumerable<Primitive>
{
    /// <summary>Bits of a primitive id that number the primitives of one element: up to 64 each.</summary>
    public const int PrimitiveIdShift = 6;

    private static readonly IComparer<Primitive> DrawOrder = Comparer<Primitive>.Create(Compare);

    private readonly List<Primitive> primitives = [];

    /// <summary>Gets how many primitives there are.</summary>
    public int Count => primitives.Count;

    /// <summary>Returns the id of the primitive numbered <paramref name="index"/> within element <paramref name="elementId"/>.</summary>
    public static int GetPrimitiveId(int elementId, int index)
    {
        return (elementId << PrimitiveIdShift) | index;
    }

    /// <summary>
    /// Adds a primitive in its place, or replaces the one with its id (which may move it, when
    /// its z-index changed).
    /// </summary>
    public void Set(Primitive primitive)
    {
        int existing = primitives.FindIndex(p => p.ElementId == primitive.ElementId);
        if (existing >= 0)
        {
            primitives.RemoveAt(existing);
        }

        int index = primitives.BinarySearch(primitive, DrawOrder);
        primitives.Insert(index >= 0 ? index : ~index, primitive);
    }

    /// <summary>
    /// Removes every primitive of the element with <paramref name="elementId"/>; for a negative
    /// id (a part of the window's frame), the one primitive with that id.
    /// </summary>
    public void RemoveElement(int elementId)
    {
        _ = primitives.RemoveAll(p => p.ElementId >= 0
            ? (p.ElementId >> PrimitiveIdShift) == elementId
            : p.ElementId == elementId);
    }

    public IEnumerator<Primitive> GetEnumerator()
    {
        return primitives.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private static int Compare(Primitive a, Primitive b)
    {
        bool aFrame = a.ElementId < 0;
        bool bFrame = b.ElementId < 0;
        if (aFrame != bFrame)
        {
            return aFrame ? -1 : 1;
        }

        int byZIndex = aFrame ? 0 : a.ZIndex.CompareTo(b.ZIndex);
        return byZIndex != 0 ? byZIndex : a.ElementId.CompareTo(b.ElementId);
    }
}
