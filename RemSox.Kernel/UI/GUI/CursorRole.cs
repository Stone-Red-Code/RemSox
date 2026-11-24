namespace RemSox.Kernel.UI.GUI;

/// <summary>
/// What the pointer shows, after the Windows pointer roles: each is one <c>.cur</c> file of the
/// cursor set (<see cref="Cursors.GetFileName"/>). Windows and controls say which they want for
/// what is under the pointer; the screen shows it.
/// </summary>
public enum CursorRole
{
    /// <summary>Normal select: the default pointer.</summary>
    Arrow,

    /// <summary>Help select.</summary>
    Help,

    /// <summary>Working in background: the pointer still works.</summary>
    Working,

    /// <summary>Busy: nothing can be done here until the work ends.</summary>
    Busy,

    /// <summary>Precision select.</summary>
    Crosshair,

    /// <summary>Text select: over text that can be typed in or selected.</summary>
    Text,

    /// <summary>Handwriting.</summary>
    Pen,

    /// <summary>Unavailable: what is under the pointer cannot be used.</summary>
    Unavailable,

    /// <summary>Vertical resize: a window's top or bottom edge.</summary>
    ResizeNorthSouth,

    /// <summary>Horizontal resize: a window's left or right edge.</summary>
    ResizeEastWest,

    /// <summary>Diagonal resize: a window's top-left or bottom-right corner.</summary>
    ResizeNorthwestSoutheast,

    /// <summary>Diagonal resize: a window's top-right or bottom-left corner.</summary>
    ResizeNortheastSouthwest,

    /// <summary>Move: a window being dragged.</summary>
    Move,

    /// <summary>Alternate select.</summary>
    Alternate,

    /// <summary>Link select: a hyperlink.</summary>
    Link,

    /// <summary>Pan: something that can be grabbed.</summary>
    Grab,

    /// <summary>Grabbing: something being dragged by hand.</summary>
    Grabbing,

    ZoomIn,

    ZoomOut,

    /// <summary>Person select.</summary>
    Person,

    /// <summary>Location select.</summary>
    Pin,
}
