using RemSox.Shared.UI;
using RemSox.Shared.UI.GUI.Rendering;
using RemSox.Kernel.UI.GUI.UIEelements;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.Windows;

/// <summary>
/// Represents a window within the GUI system, managing its state, UI elements, and interactions.
/// </summary>
public sealed class Window(string title, int processId, int id, Screen screen)
{
    /// <summary>Gets the screen the window is on: its process's session's.</summary>
    public Screen Screen { get; } = screen;

    /// <summary> Gets the unique identifier for this window. </summary>
    public int Id { get; } = id;

    /// <summary> Gets the ID of the process that owns this window. </summary>
    public int ProcessId { get; } = processId;

    /// <summary> Gets or sets the title of the window. </summary>
    public string Title { get; set; } = title;

    /// <summary> Gets or sets the Z-order index of the window (higher means more foreground). </summary>
    public int ZIndex { get; set; }

    /// <summary> Gets or sets a value indicating whether changes should automatically trigger a redraw. </summary>
    public bool AutoFlush { get; set; } = false;

    /// <summary> Event raised when a keyboard event is handled by this window. </summary>
    public event Action<Sys.Input.KeyEvent>? OnKeyEvent;

    /// <summary> Event raised when a mouse event is handled by this window. </summary>
    public event Action<MouseEvent>? OnMouseEvent;

    /// <summary>
    /// Event raised when the user clicks the close button. With no handler the window manager
    /// closes the window itself (and stops the owning process once it has no windows left); a
    /// handler takes over and must close the window with <see cref="Screen.CloseWindow"/>.
    /// </summary>
    public event Action<Window>? OnCloseRequested;

    /// <summary> Gets or sets whether this window is currently focused. </summary>
    public bool IsFocused
    {
        get => Screen.IsWindowFocused(this);
        set => Screen.FocusWindow(value ? this : null);
    }

    /// <summary> Gets or sets whether the window is visible. </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary> Gets or sets whether the window is resizable by the user. </summary>
    public bool IsResizable { get; set; } = true;

    /// <summary> Gets or sets whether the window can be dragged by the user. </summary>
    public bool IsDraggable { get; set; } = true;

    /// <summary> Gets or sets whether the window has a title bar and border. </summary>
    public bool HasChrome { get; set; } = true;

    /// <summary>
    /// Gets or sets the layer the window is stacked in: every window of a higher layer is drawn
    /// above every window of a lower one, and <see cref="ZIndex"/> orders windows within a layer.
    /// Only <see cref="WindowLayer.Normal"/> windows take keyboard focus when clicked, so the
    /// taskbar and start menu (<see cref="WindowLayer.Foreground"/>) leave it with the app.
    /// </summary>
    public WindowLayer Layer { get; set; } = WindowLayer.Normal;

    /// <summary> Gets whether the window is minimized: hidden, but still listed on the taskbar. </summary>
    public bool IsMinimized { get; internal set; }

    /// <summary> Gets whether the window fills the desktop work area. </summary>
    public bool IsMaximized { get; private set; }

    /// <summary> Title bar buttons a click can land on. </summary>
    public enum ChromeButton { None, Minimize, Maximize, Close }

    /// <summary> Gets or sets the position of the window. </summary>
    public Point Position { get; set; }

    /// <summary> Gets or sets the size of the window. </summary>
    public Size Size { get; set; }

    /// <summary> Gets whether the window is currently being dragged. </summary>
    public bool IsDragging => currentInteraction == InteractionMode.Drag;

    /// <summary>
    /// How opaque the window is composited: see-through while it is dragged, so what is under it
    /// stays visible.
    /// </summary>
    public byte Opacity => IsDragging ? DragOpacity : (byte)255;

    private const byte DragOpacity = 190;

    /// <summary> Gets whether the window is currently being resized. </summary>
    public bool IsResizing => currentInteraction is InteractionMode.ResizeTop or InteractionMode.ResizeBottom or InteractionMode.ResizeLeft or InteractionMode.ResizeRight or InteractionMode.ResizeTopLeft or InteractionMode.ResizeTopRight or InteractionMode.ResizeBottomLeft or InteractionMode.ResizeBottomRight;

    private readonly Dictionary<int, UIElement> uiElements = [];
    private readonly Dictionary<int, Control> controls = [];
    private Control? focusedControl = null;
    private Control? capturedControl = null;

    private int nextUIElementId = 1;

    private enum InteractionMode { None, ChromeButtonPress, Drag, ResizeTop, ResizeBottom, ResizeLeft, ResizeRight, ResizeTopLeft, ResizeTopRight, ResizeBottomLeft, ResizeBottomRight }
    private InteractionMode currentInteraction = InteractionMode.None;
    private Rectangle interactionStartBounds;
    private Point interactionStartPointer;
    private Point dragOffset;
    private ChromeButton pressedChromeButton = ChromeButton.None;
    private Rectangle restoreBounds;

    private Point lastRenderedPosition = new(-1, -1);
    private Size lastRenderedSize = new(-1, -1);
    private bool lastRenderedIsFocused = false;
    private string lastRenderedTitle = string.Empty;
    private int lastRenderedZIndex = -1;
    private WindowLayer lastRenderedLayer = WindowLayer.Normal;
    private byte lastRenderedOpacity = 255;
    private bool lastRenderedIsMaximized = false;
    private bool isFirstRender = true;

    /// <summary>
    /// Creates and registers a new UI element within this window.
    /// </summary>
    public T CreateUIElement<T>(Action<T>? options = null) where T : UIElement, new()
    {
        int uiElementId = GetNextUIElementId();

        T uiElement = new()
        {
            Id = uiElementId,
            Window = this,
        };

        options?.Invoke(uiElement);

        uiElement.PropertyChanged += (sender, args) =>
        {
            if (AutoFlush)
            {
                Flush();
            }
        };

        uiElements.Add(uiElementId, uiElement);

        if (uiElement is Control control)
        {
            controls.Add(uiElementId, control);
        }

        if (AutoFlush)
        {
            Flush();
        }

        return uiElement;
    }

    public void RemoveUIElement(int elementId)
    {
        bool removed = false;

        if (uiElements.Remove(elementId))
        {
            removed = true;
        }

        if (controls.Remove(elementId))
        {
            removed = true;
        }

        if (removed)
        {
            Screen.Output.Render([new RemovePrimitivesCommand
            {
                WindowId = Id,
                ElementId = elementId,
            }]);

            if (AutoFlush)
            {
                Flush();
            }
        }
    }

    /// <summary>
    /// Invalidates the window state, forcing a full redraw on the next flush.
    /// </summary>
    public void Invalidate()
    {
        isFirstRender = true;
        Flush();
    }

    // The frame's primitives. They are drawn before every element, in the order of these ids
    // (PrimitiveList), so each comes before what it draws under.
    private const int ChromeClientBg = -13;
    private const int ChromeTitleBg = -12;
    private const int ChromeTitleText = -11;
    private const int ChromeCloseBg = -10;
    private const int ChromeCloseGlyphA = -9;
    private const int ChromeCloseGlyphB = -8;
    private const int ChromeMaximizeBg = -7;
    private const int ChromeMaximizeGlyphBack = -6;
    private const int ChromeMaximizeGlyphFill = -5;
    private const int ChromeMaximizeGlyphFront = -4;
    private const int ChromeMinimizeBg = -3;
    private const int ChromeMinimizeGlyph = -2;
    private const int ChromeBorder = -1;

    /// <summary> Height of the title bar, in pixels. </summary>
    public const int TitleBarHeight = 18;

    private const int ChromeButtonWidth = 20;
    private const int ChromeButtonHeight = 16;

    /// <summary>
    /// Sends current window and element state to the renderer (delta-only).
    /// </summary>
    public void Flush()
    {
        if (!IsVisible || IsMinimized)
        {
            return;
        }

        List<UIElement> elementsCopy;
        elementsCopy = [.. uiElements.Values];

        bool sizeChanged = Size != lastRenderedSize;
        bool positionChanged = Position != lastRenderedPosition;
        bool zIndexChanged = ZIndex != lastRenderedZIndex || Layer != lastRenderedLayer;
        bool titleChanged = Title != lastRenderedTitle;
        bool focusChanged = IsFocused != lastRenderedIsFocused;
        bool opacityChanged = Opacity != lastRenderedOpacity;
        bool maximizedChanged = IsMaximized != lastRenderedIsMaximized;
        bool chromeChanged = isFirstRender || sizeChanged || titleChanged || focusChanged || maximizedChanged;

        List<RenderCommand> commands = [];

        // --- Structural commands ---
        if (isFirstRender || sizeChanged || zIndexChanged)
        {
            commands.Add(new CreateWindowCommand
            {
                WindowId = Id,
                ElementId = Id,
                Position = Position,
                Size = Size,
                ZIndex = ZIndex,
                Layer = Layer,
            });
        }
        else if (positionChanged)
        {
            commands.Add(new MoveWindowCommand
            {
                WindowId = Id,
                ElementId = Id,
                Position = Position,
            });
        }

        if (opacityChanged || (isFirstRender && Opacity != 255))
        {
            commands.Add(new SetWindowOpacityCommand
            {
                WindowId = Id,
                ElementId = Id,
                Opacity = Opacity,
            });
        }

        // --- Chrome primitives (title bar, border, background) ---
        if (chromeChanged && HasChrome)
        {
            ChromePrimitives(commands);
        }

        // --- Child element primitives (delta) ---
        if (isFirstRender)
        {
            foreach (UIElement element in elementsCopy)
            {
                AddPrimitives(commands, element);
            }
        }
        else
        {
            foreach (UIElement element in elementsCopy)
            {
                if (!element.AnyPropertyChanged)
                {
                    continue;
                }

                // Remove its old primitives (it may now have fewer), then send the new ones; the
                // renderers put them back where the element belongs (PrimitiveList).
                // TODO: optimize by diffing properties and only updating what changed instead of full remove+add
                commands.Add(new RemovePrimitivesCommand
                {
                    WindowId = Id,
                    ElementId = element.Id,
                });
                AddPrimitives(commands, element);
            }
        }

        if (commands.Count > 0)
        {
            Screen.Output.Render(commands);
            lastRenderedPosition = Position;
            lastRenderedSize = Size;
            lastRenderedIsFocused = IsFocused;
            lastRenderedTitle = Title;
            lastRenderedZIndex = ZIndex;
            lastRenderedLayer = Layer;
            lastRenderedOpacity = Opacity;
            lastRenderedIsMaximized = IsMaximized;
            isFirstRender = false;
        }
    }

    /// <summary>Adds an element's primitives, each with its z-index, and marks the element as sent.</summary>
    private void AddPrimitives(List<RenderCommand> commands, UIElement element)
    {
        foreach (Primitive primitive in element.ToPrimitives(Id))
        {
            primitive.ZIndex = element.ZIndex;
            primitive.Clip = element.ClipBounds;
            commands.Add(primitive);
        }

        element.ClearChangedProperties();
    }

    private void ChromePrimitives(List<RenderCommand> commands)
    {
        Color border = IsFocused ? Color.White : Color.DarkGray;
        Color title = IsFocused ? Color.FromArgb(0, 120, 215) : Color.FromArgb(80, 80, 80);

        // Client area background
        commands.Add(new FilledRectangle
        {
            WindowId = Id,
            ElementId = ChromeClientBg,
            Position = Point.Empty,
            Color = Color.FromArgb(32, 32, 32),
            Size = Size,
        });

        // Title bar background
        commands.Add(new FilledRectangle
        {
            WindowId = Id,
            ElementId = ChromeTitleBg,
            Position = Point.Empty,
            Color = title,
            Size = new Size(Size.Width, 18),
        });

        // Title bar text
        if (!string.IsNullOrEmpty(Title))
        {
            commands.Add(new TextPrimitive
            {
                WindowId = Id,
                ElementId = ChromeTitleText,
                Position = new Point(4, 2),
                Color = Color.White,
                Content = Title,
                FontSize = 18,
                MaxWidth = Math.Max(0, Size.Width - 8 - ChromeButtonsWidth()),
            });
        }

        ChromeButtonPrimitives(commands, title);

        // Window border
        commands.Add(new RectangleBorder
        {
            WindowId = Id,
            ElementId = ChromeBorder,
            Position = Point.Empty,
            Color = border,
            Size = Size,
        });
    }

    private void ChromeButtonPrimitives(List<RenderCommand> commands, Color titleColor)
    {
        Color buttonColor = Color.FromArgb(
            Math.Min(255, titleColor.R + 30),
            Math.Min(255, titleColor.G + 30),
            Math.Min(255, titleColor.B + 30));
        Color glyph = Color.White;

        Rectangle close = GetChromeButtonBounds(ChromeButton.Close);
        commands.Add(FilledRectPrimitive(ChromeCloseBg, close, IsFocused ? Color.FromArgb(196, 43, 28) : buttonColor));
        commands.Add(LinePrimitive(ChromeCloseGlyphA, new Point(close.X + 6, close.Y + 4), new Point(close.X + 13, close.Y + 11), glyph));
        commands.Add(LinePrimitive(ChromeCloseGlyphB, new Point(close.X + 13, close.Y + 4), new Point(close.X + 6, close.Y + 11), glyph));

        Rectangle maximize = GetChromeButtonBounds(ChromeButton.Maximize);
        if (!maximize.IsEmpty)
        {
            commands.Add(FilledRectPrimitive(ChromeMaximizeBg, maximize, buttonColor));

            // The same three primitives in both states, so toggling only replaces them.
            if (IsMaximized)
            {
                // "Restore" glyph: a square behind another
                Rectangle front = new(maximize.X + 5, maximize.Y + 6, 7, 7);
                commands.Add(RectBorderPrimitive(ChromeMaximizeGlyphBack, new Rectangle(maximize.X + 8, maximize.Y + 3, 7, 7), glyph));
                commands.Add(FilledRectPrimitive(ChromeMaximizeGlyphFill, front, buttonColor));
                commands.Add(RectBorderPrimitive(ChromeMaximizeGlyphFront, front, glyph));
            }
            else
            {
                Rectangle square = new(maximize.X + 6, maximize.Y + 4, 8, 8);
                commands.Add(RectBorderPrimitive(ChromeMaximizeGlyphBack, square, glyph));
                commands.Add(FilledRectPrimitive(ChromeMaximizeGlyphFill, new Rectangle(square.Location, Size.Empty), buttonColor));
                commands.Add(RectBorderPrimitive(ChromeMaximizeGlyphFront, square, glyph));
            }
        }

        Rectangle minimize = GetChromeButtonBounds(ChromeButton.Minimize);
        commands.Add(FilledRectPrimitive(ChromeMinimizeBg, minimize, buttonColor));
        commands.Add(LinePrimitive(ChromeMinimizeGlyph, new Point(minimize.X + 6, minimize.Y + 11), new Point(minimize.X + 13, minimize.Y + 11), glyph));
    }

    private RenderCommand FilledRectPrimitive(int elementId, Rectangle bounds, Color color)
    {
        return new FilledRectangle
        {
            WindowId = Id,
            ElementId = elementId,
            Position = bounds.Location,
            Color = color,
            Size = bounds.Size,
        };
    }

    private RenderCommand RectBorderPrimitive(int elementId, Rectangle bounds, Color color)
    {
        return new RectangleBorder
        {
            WindowId = Id,
            ElementId = elementId,
            Position = bounds.Location,
            Color = color,
            Size = bounds.Size,
        };
    }

    private RenderCommand LinePrimitive(int elementId, Point start, Point end, Color color)
    {
        return new LinePrimitive
        {
            WindowId = Id,
            ElementId = elementId,
            Position = start,
            Color = color,
            End = end,
        };
    }

    /// <summary> Width the title bar buttons take up at the right of the title bar. </summary>
    private int ChromeButtonsWidth()
    {
        return HasChrome ? ((IsResizable ? 3 : 2) * ChromeButtonWidth) + 4 : 0;
    }

    /// <summary>
    /// Gets a title bar button's bounds, relative to the window; empty when the window does not
    /// show that button (no chrome, or a maximize button on a window that cannot be resized).
    /// </summary>
    public Rectangle GetChromeButtonBounds(ChromeButton button)
    {
        if (!HasChrome)
        {
            return Rectangle.Empty;
        }

        int slot = button switch
        {
            ChromeButton.Close => 0,
            ChromeButton.Maximize when IsResizable => 1,
            ChromeButton.Minimize => IsResizable ? 2 : 1,
            _ => -1,
        };

        if (slot < 0)
        {
            return Rectangle.Empty;
        }

        int x = Size.Width - 2 - ((slot + 1) * ChromeButtonWidth);
        return new Rectangle(x, 1, ChromeButtonWidth, ChromeButtonHeight);
    }

    private ChromeButton HitTestChromeButton(Point pointerPosition)
    {
        Point local = new(pointerPosition.X - Position.X, pointerPosition.Y - Position.Y);

        foreach (ChromeButton button in new[] { ChromeButton.Close, ChromeButton.Maximize, ChromeButton.Minimize })
        {
            Rectangle bounds = GetChromeButtonBounds(button);
            if (!bounds.IsEmpty && bounds.Contains(local))
            {
                return button;
            }
        }

        return ChromeButton.None;
    }

    /// <summary> Raises <see cref="OnCloseRequested"/>; returns false when nothing handles it. </summary>
    internal bool RaiseCloseRequested()
    {
        Action<Window>? handler = OnCloseRequested;
        if (handler is null)
        {
            return false;
        }

        handler(this);
        return true;
    }

    /// <summary> Fills <paramref name="workArea"/>, or returns to the bounds it had before. </summary>
    internal void SetMaximized(bool maximized, Rectangle workArea)
    {
        if (maximized == IsMaximized)
        {
            return;
        }

        if (maximized)
        {
            restoreBounds = new Rectangle(Position, Size);
            Position = workArea.Location;
            Size = workArea.Size;
        }
        else
        {
            Position = restoreBounds.Location;
            Size = restoreBounds.Size;
        }

        IsMaximized = maximized;
        Flush();
    }

    /// <summary>
    /// Gives <paramref name="control"/> the keyboard: the window's key events go to it from now
    /// on (after <see cref="OnKeyEvent"/>), until another control is clicked or focused. Null
    /// leaves no control focused.
    /// </summary>
    public void Focus(Control? control)
    {
        if (control == focusedControl)
        {
            return;
        }

        if (focusedControl is not null)
        {
            focusedControl.IsFocused = false;
        }

        focusedControl = control;

        if (control is not null)
        {
            control.IsFocused = true;
        }
    }

    /// <summary> Dispatches a key event to the window's registered event handlers. </summary>
    public void HandleKeyEvent(Sys.Input.KeyEvent keyEvent)
    {
        OnKeyEvent?.Invoke(keyEvent);

        if (focusedControl is { } control)
        {
            control.HandleKeyEvent(keyEvent);
            FlushIfChanged(control);
        }
    }

    /// <summary> Dispatches a mouse event to the window's registered event handlers. </summary>
    public void HandleMouseEvent(MouseEvent mouseEvent)
    {
        Point local = new(mouseEvent.X - Position.X, mouseEvent.Y - Position.Y);

        if (mouseEvent.Type == MouseEventType.ButtonDown && mouseEvent.Button == MouseButton.Left)
        {
            capturedControl = HitTestControl(local);
            Focus(capturedControl);
        }

        // After the click has moved the focus, so a handler that focuses a control keeps it focused.
        OnMouseEvent?.Invoke(mouseEvent);

        Control? target = capturedControl ?? HitTestControl(local);

        // The release still goes to the control that captured the press, wherever it happens,
        // so a drag that ends outside the control (a slider, a scroll bar's thumb) ends there.
        if (mouseEvent.Type == MouseEventType.ButtonUp && mouseEvent.Button == MouseButton.Left)
        {
            capturedControl = null;
        }

        if (target is null)
        {
            return;
        }

        MouseEvent localEvent = mouseEvent with
        {
            X = local.X,
            Y = local.Y
        };

        target.HandleMouseEvent(localEvent);
        FlushIfChanged(target);
    }

    /// <summary>
    /// Checks if a pointer position starts an interaction (drag/resize) and handles focus.
    /// </summary>
    public bool TryBeginInteract(Point pointerPosition)
    {
        if (!IsVisible || IsMinimized)
        {
            return false;
        }

        bool inBounds = Contains(pointerPosition);

        pressedChromeButton = HitTestChromeButton(pointerPosition);
        currentInteraction = pressedChromeButton != ChromeButton.None
            ? InteractionMode.ChromeButtonPress
            : HitTestResizeEdge(pointerPosition);

        if (currentInteraction == InteractionMode.None && IsDraggable && !IsMaximized && IsPointInTitleBar(pointerPosition))
        {
            currentInteraction = InteractionMode.Drag;
            dragOffset = new Point(pointerPosition.X - Position.X, pointerPosition.Y - Position.Y);
            Flush(); // turn translucent
        }

        if (currentInteraction != InteractionMode.None || inBounds)
        {
            if (currentInteraction != InteractionMode.None)
            {
                interactionStartBounds = new Rectangle(Position, Size);
                interactionStartPointer = pointerPosition;
            }

            if (Layer == WindowLayer.Normal)
            {
                Screen.FocusWindow(this);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets whether <paramref name="pointerPosition"/> (on the screen) is on the window, its
    /// edges included, while it is shown.
    /// </summary>
    public bool Contains(Point pointerPosition)
    {
        return IsVisible && !IsMinimized
            && pointerPosition.X >= Position.X && pointerPosition.X <= Position.X + Size.Width
            && pointerPosition.Y >= Position.Y && pointerPosition.Y <= Position.Y + Size.Height;
    }

    /// <summary>
    /// Returns the pointer to show at <paramref name="pointerPosition"/> (on the screen): while
    /// the window is dragged or resized, what that interaction shows wherever the pointer is;
    /// otherwise a resize pointer on an edge that resizes, the normal pointer on the title bar,
    /// and over the content what the control under the pointer (or the one a press is held on)
    /// asks for.
    /// </summary>
    public CursorRole GetCursorAt(Point pointerPosition)
    {
        InteractionMode mode = currentInteraction != InteractionMode.None
            ? currentInteraction
            : HitTestChromeButton(pointerPosition) != ChromeButton.None ? InteractionMode.ChromeButtonPress : HitTestResizeEdge(pointerPosition);

        switch (mode)
        {
            case InteractionMode.Drag:
                return CursorRole.Move;
            case InteractionMode.ResizeTop or InteractionMode.ResizeBottom:
                return CursorRole.ResizeNorthSouth;
            case InteractionMode.ResizeLeft or InteractionMode.ResizeRight:
                return CursorRole.ResizeEastWest;
            case InteractionMode.ResizeTopLeft or InteractionMode.ResizeBottomRight:
                return CursorRole.ResizeNorthwestSoutheast;
            case InteractionMode.ResizeTopRight or InteractionMode.ResizeBottomLeft:
                return CursorRole.ResizeNortheastSouthwest;
            case InteractionMode.ChromeButtonPress:
                return CursorRole.Arrow;
        }

        if (IsPointInTitleBar(pointerPosition))
        {
            return CursorRole.Arrow;
        }

        Point local = new(pointerPosition.X - Position.X, pointerPosition.Y - Position.Y);
        return (capturedControl ?? HitTestControl(local))?.Cursor ?? CursorRole.Arrow;
    }

    /// <summary>
    /// Returns the resize a press at <paramref name="pointerPosition"/> would start: on a corner
    /// or an edge of a window that can be resized now; none elsewhere.
    /// </summary>
    private InteractionMode HitTestResizeEdge(Point pointerPosition)
    {
        if (!IsResizable || IsMaximized || !Contains(pointerPosition))
        {
            return InteractionMode.None;
        }

        const int resizeMargin = 5;

        bool onLeft = pointerPosition.X <= Position.X + resizeMargin;
        bool onRight = pointerPosition.X >= Position.X + Size.Width - resizeMargin;
        bool onTop = pointerPosition.Y <= Position.Y + resizeMargin;
        bool onBottom = pointerPosition.Y >= Position.Y + Size.Height - resizeMargin;

        return (onTop, onBottom, onLeft, onRight) switch
        {
            (true, _, true, _) => InteractionMode.ResizeTopLeft,
            (true, _, _, true) => InteractionMode.ResizeTopRight,
            (_, true, true, _) => InteractionMode.ResizeBottomLeft,
            (_, true, _, true) => InteractionMode.ResizeBottomRight,
            (_, _, true, _) => InteractionMode.ResizeLeft,
            (_, _, _, true) => InteractionMode.ResizeRight,
            (true, _, _, _) => InteractionMode.ResizeTop,
            (_, true, _, _) => InteractionMode.ResizeBottom,
            _ => InteractionMode.None,
        };
    }

    /// <summary>
    /// Updates the drag or resize interaction based on the current pointer position.
    /// </summary>
    public void UpdateInteraction(Point pointerPosition, Size screenSize)
    {
        if (currentInteraction == InteractionMode.Drag)
        {
            int newX = pointerPosition.X - dragOffset.X;
            int newY = pointerPosition.Y - dragOffset.Y;

            // Clamp left and top edges
            newX = Math.Max(0, newX);
            newY = Math.Max(0, newY);

            // Clamp right and bottom edges
            newX = Math.Min(screenSize.Width - Size.Width, newX);
            newY = Math.Min(screenSize.Height - Size.Height, newY);

            Position = new Point(newX, newY);
            Flush();
        }
        else if (currentInteraction is not InteractionMode.None and not InteractionMode.ChromeButtonPress)
        {
            int dx = pointerPosition.X - interactionStartPointer.X;
            int dy = pointerPosition.Y - interactionStartPointer.Y;

            int newX = interactionStartBounds.X;
            int newY = interactionStartBounds.Y;
            int newW = interactionStartBounds.Width;
            int newH = interactionStartBounds.Height;

            const int minWidth = 100;
            const int minHeight = 50;

            if (currentInteraction is InteractionMode.ResizeRight or InteractionMode.ResizeBottomRight or InteractionMode.ResizeTopRight)
            {
                newW = Math.Max(minWidth, interactionStartBounds.Width + dx);
                newW = Math.Min(newW, screenSize.Width - newX);
            }
            if (currentInteraction is InteractionMode.ResizeBottom or InteractionMode.ResizeBottomRight or InteractionMode.ResizeBottomLeft)
            {
                newH = Math.Max(minHeight, interactionStartBounds.Height + dy);
                newH = Math.Min(newH, screenSize.Height - newY);
            }
            if (currentInteraction is InteractionMode.ResizeLeft or InteractionMode.ResizeBottomLeft or InteractionMode.ResizeTopLeft)
            {
                int maxDx = interactionStartBounds.Width - minWidth;
                int clampedDx = Math.Min(dx, maxDx);
                newX = Math.Max(0, interactionStartBounds.X + clampedDx);
                newW = interactionStartBounds.X + interactionStartBounds.Width - newX;
            }
            if (currentInteraction is InteractionMode.ResizeTop or InteractionMode.ResizeTopLeft or InteractionMode.ResizeTopRight)
            {
                int maxDy = interactionStartBounds.Height - minHeight;
                int clampedDy = Math.Min(dy, maxDy);
                newY = Math.Max(0, interactionStartBounds.Y + clampedDy);
                newH = interactionStartBounds.Y + interactionStartBounds.Height - newY;
            }

            Position = new Point(newX, newY);
            Size = new Size(newW, newH);
            Flush();
        }
    }

    /// <summary>
    /// Ends the current drag, resize or title bar button press. Returns the title bar button that
    /// was clicked (pressed and released over the same button), or <see cref="ChromeButton.None"/>.
    /// </summary>
    public ChromeButton EndInteraction(Point pointerPosition)
    {
        bool wasDragging = IsDragging;
        ChromeButton clicked = currentInteraction == InteractionMode.ChromeButtonPress
            && HitTestChromeButton(pointerPosition) == pressedChromeButton
                ? pressedChromeButton
                : ChromeButton.None;

        currentInteraction = InteractionMode.None;
        pressedChromeButton = ChromeButton.None;

        if (wasDragging)
        {
            Flush(); // opaque again
        }

        return clicked;
    }

    /// <summary>
    /// Draws a control that changed itself in answer to input (a typed character, a ticked box),
    /// so it shows at once rather than whenever its owner next flushes.
    /// </summary>
    private void FlushIfChanged(Control control)
    {
        if (control.AnyPropertyChanged)
        {
            Flush();
        }
    }

    private Control? HitTestControl(Point windowRelativePosition)
    {
        // top-most last (simple Z-order assumption: insertion order)
        foreach (Control control in controls.Values.Reverse())
        {
            Rectangle bounds = new(
                control.Position.X,
                control.Position.Y,
                control.Size.Width,
                control.Size.Height
            );

            // What a scroll panel has scrolled out of view cannot be clicked either.
            if (bounds.Contains(windowRelativePosition) && control.ClipBounds?.Contains(windowRelativePosition) != false)
            {
                return control;
            }
        }

        return null;
    }

    private bool IsPointInTitleBar(Point pointerPosition)
    {
        return HasChrome
            && pointerPosition.X >= Position.X
            && pointerPosition.X < Position.X + Size.Width
            && pointerPosition.Y >= Position.Y
            && pointerPosition.Y < Position.Y + TitleBarHeight;
    }

    private int GetNextUIElementId()
    {
        return nextUIElementId++;
    }
}