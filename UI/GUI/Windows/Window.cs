using Cosmos.Kernel.System.Graphics;
using RemSox.UI.GUI.Rendering;
using RemSox.UI.GUI.UIEelements;

using System.Drawing;

namespace RemSox.UI.GUI.Windows;

/// <summary>
/// Represents a window within the GUI system, managing its state, UI elements, and interactions.
/// </summary>
public sealed class Window(string title, int processId, int id, IRenderSource renderSource)
{
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
    public event Action<Sys.Keyboard.KeyEvent>? OnKeyEvent;

    /// <summary> Dispatches a key event to the window's registered event handlers. </summary>
    public void HandleKeyEvent(Sys.Keyboard.KeyEvent keyEvent)
    {
        OnKeyEvent?.Invoke(keyEvent);
    }

    /// <summary> Gets or sets whether this window is currently focused. </summary>
    public bool IsFocused
    {
        get => WindowManager.IsWindowFocused(this); set => WindowManager.FocusWindow(value ? this : null);
    }

    /// <summary> Gets or sets whether the window is visible. </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary> Gets or sets whether the window is resizable by the user. </summary>
    public bool IsResizable { get; set; } = true;

    /// <summary> Gets or sets whether the window can be dragged by the user. </summary>
    public bool IsDraggable { get; set; } = true;

    /// <summary> Gets or sets the position of the window. </summary>
    public Point Position { get; set; }

    /// <summary> Gets or sets the size of the window. </summary>
    public Size Size { get; set; }

    /// <summary> Gets whether the window is currently being dragged. </summary>
    public bool IsDragging => currentInteraction == InteractionMode.Drag;

    private readonly Lock uiElementsLock = new();
    private readonly Dictionary<int, UIElement> uiElements = [];

    private int nextUIElementId = 1;

    private enum InteractionMode { None, Drag, ResizeTop, ResizeBottom, ResizeLeft, ResizeRight, ResizeTopLeft, ResizeTopRight, ResizeBottomLeft, ResizeBottomRight }
    private InteractionMode currentInteraction = InteractionMode.None;
    private Rectangle interactionStartBounds;
    private Point interactionStartPointer;
    private Point dragOffset;

    private Point lastRenderedPosition = new(-1, -1);
    private Size lastRenderedSize = new(-1, -1);
    private bool lastRenderedIsFocused = false;
    private string lastRenderedTitle = string.Empty;
    private int lastRenderedZIndex = -1;
    private bool isFirstRender = true;

    /// <summary>
    /// Creates and registers a new UI element within this window.
    /// </summary>
    public T CreateUIElement<T>(Action<T>? options = null) where T : UIElement, new()
    {
        int uiElementId = GetNextUIElementId();

        T uiElement = new()
        {
            Id = uiElementId
        };

        options?.Invoke(uiElement);

        uiElement.PropertyChanged += (sender, args) =>
        {
            if (AutoFlush)
            {
                Flush();
            }
        };

        if (AutoFlush)
        {
            Flush();
        }

        lock (uiElementsLock)
        {
            uiElements.Add(uiElementId, uiElement);
        }

        return uiElement;
    }

    /// <summary>
    /// Invalidates the window state, forcing a full redraw on the next flush.
    /// </summary>
    public void Invalidate()
    {
        isFirstRender = true;
        Flush();
    }

    /// <summary>
    /// Sends current window and element state to the renderer.
    /// </summary>
    public void Flush()
    {
        if (!IsVisible)
        {
            return;
        }

        bool anyChildChanged;
        List<UIElement> elementsCopy;

        lock (uiElementsLock)
        {
            anyChildChanged = uiElements.Values.Any(e => e.AnyPropertyChanged);
            elementsCopy = uiElements.Values.ToList();
        }

        bool windowStateChanged = isFirstRender || Size != lastRenderedSize || IsFocused != lastRenderedIsFocused || Title != lastRenderedTitle;
        bool positionChanged = Position != lastRenderedPosition;
        bool zIndexChanged = ZIndex != lastRenderedZIndex;

        bool fullRedraw = windowStateChanged || anyChildChanged;

        List<RenderCommand> commands = [];

        if (fullRedraw)
        {
            commands.Add(new RenderCommand
            {
                WindowId = Id,
                ElementId = Id,
                ElementType = "Window",
                Position = Position,
                Properties = new Dictionary<string, object?>
                {
                    [nameof(Title)] = Title,
                    [nameof(Size)] = Size,
                    [nameof(IsFocused)] = IsFocused,
                    [nameof(IsResizable)] = IsResizable,
                    [nameof(IsDraggable)] = IsDraggable,
                    [nameof(ZIndex)] = ZIndex
                }
            });

            foreach (UIElement element in elementsCopy)
            {
                commands.Add(new RenderCommand
                {
                    WindowId = Id,
                    ElementId = element.Id,
                    ElementType = element.Type,
                    Position = element.Position,
                    Properties = element.AllProperties
                });
                element.ClearChangedProperties();
            }
        }
        else if (positionChanged || zIndexChanged)
        {
            commands.Add(new RenderCommand
            {
                WindowId = Id,
                ElementId = Id,
                ElementType = "WindowMove",
                Position = Position,
                Properties = new Dictionary<string, object?>
                {
                    [nameof(ZIndex)] = ZIndex
                }
            });
        }

        if (commands.Count > 0)
        {
            renderSource.Render(commands);
            lastRenderedPosition = Position;
            lastRenderedSize = Size;
            lastRenderedIsFocused = IsFocused;
            lastRenderedTitle = Title;
            lastRenderedZIndex = ZIndex;
            isFirstRender = false;
        }
    }

    /// <summary>
    /// Checks if a pointer position starts an interaction (drag/resize) and handles focus.
    /// </summary>
    public bool TryBeginInteract(Point pointerPosition)
    {
        if (!IsVisible)
        {
            return false;
        }

        const int resizeMargin = 5;

        bool onLeft = pointerPosition.X >= Position.X && pointerPosition.X <= Position.X + resizeMargin;
        bool onRight = pointerPosition.X >= Position.X + Size.Width - resizeMargin && pointerPosition.X <= Position.X + Size.Width;
        bool onTop = pointerPosition.Y >= Position.Y && pointerPosition.Y <= Position.Y + resizeMargin;
        bool onBottom = pointerPosition.Y >= Position.Y + Size.Height - resizeMargin && pointerPosition.Y <= Position.Y + Size.Height;

        bool inBounds = pointerPosition.X >= Position.X && pointerPosition.X <= Position.X + Size.Width &&
                        pointerPosition.Y >= Position.Y && pointerPosition.Y <= Position.Y + Size.Height;

        currentInteraction = InteractionMode.None;

        if (IsResizable)
        {
            if (onTop && onLeft)
            {
                currentInteraction = InteractionMode.ResizeTopLeft;
            }
            else if (onTop && onRight)
            {
                currentInteraction = InteractionMode.ResizeTopRight;
            }
            else if (onBottom && onLeft)
            {
                currentInteraction = InteractionMode.ResizeBottomLeft;
            }
            else if (onBottom && onRight)
            {
                currentInteraction = InteractionMode.ResizeBottomRight;
            }
            else if (onLeft && inBounds)
            {
                currentInteraction = InteractionMode.ResizeLeft;
            }
            else if (onRight && inBounds)
            {
                currentInteraction = InteractionMode.ResizeRight;
            }
            else if (onTop && inBounds)
            {
                currentInteraction = InteractionMode.ResizeTop;
            }
            else if (onBottom && inBounds)
            {
                currentInteraction = InteractionMode.ResizeBottom;
            }
        }

        if (currentInteraction == InteractionMode.None && IsDraggable && IsPointInTitleBar(pointerPosition))
        {
            currentInteraction = InteractionMode.Drag;
            dragOffset = new Point(pointerPosition.X - Position.X, pointerPosition.Y - Position.Y);
        }

        if (currentInteraction != InteractionMode.None || inBounds)
        {
            if (currentInteraction != InteractionMode.None)
            {
                interactionStartBounds = new Rectangle(Position, Size);
                interactionStartPointer = pointerPosition;
            }

            WindowManager.FocusWindow(this);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Updates the drag or resize interaction based on the current pointer position.
    /// </summary>
    public void UpdateInteraction(Point pointerPosition, Point screenSize)
    {
        if (currentInteraction == InteractionMode.Drag)
        {
            int newX = pointerPosition.X - dragOffset.X;
            int newY = pointerPosition.Y - dragOffset.Y;

            // Clamp left and top edges
            newX = Math.Max(0, newX);
            newY = Math.Max(0, newY);

            // Clamp right and bottom edges
            newX = Math.Min(screenSize.X - Size.Width, newX);
            newY = Math.Min(screenSize.Y - Size.Height, newY);

            Position = new Point(newX, newY);
            Flush();
        }
        else if (currentInteraction != InteractionMode.None)
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
                newW = Math.Min(newW, screenSize.X - newX);
            }
            if (currentInteraction is InteractionMode.ResizeBottom or InteractionMode.ResizeBottomRight or InteractionMode.ResizeBottomLeft)
            {
                newH = Math.Max(minHeight, interactionStartBounds.Height + dy);
                newH = Math.Min(newH, screenSize.Y - newY);
            }
            if (currentInteraction is InteractionMode.ResizeLeft or InteractionMode.ResizeBottomLeft or InteractionMode.ResizeTopLeft)
            {
                int maxDx = interactionStartBounds.Width - minWidth;
                int clampedDx = Math.Min(dx, maxDx);
                newX = Math.Max(0, interactionStartBounds.X + clampedDx);
                newW = (interactionStartBounds.X + interactionStartBounds.Width) - newX;
            }
            if (currentInteraction is InteractionMode.ResizeTop or InteractionMode.ResizeTopLeft or InteractionMode.ResizeTopRight)
            {
                int maxDy = interactionStartBounds.Height - minHeight;
                int clampedDy = Math.Min(dy, maxDy);
                newY = Math.Max(0, interactionStartBounds.Y + clampedDy);
                newH = (interactionStartBounds.Y + interactionStartBounds.Height) - newY;
            }

            Position = new Point(newX, newY);
            Size = new Size(newW, newH);
            Flush();
        }
    }

    /// <summary>
    /// Ends the current drag or resize interaction.
    /// </summary>
    public void EndInteraction()
    {
        currentInteraction = InteractionMode.None;
    }

    private bool IsPointInTitleBar(Point pointerPosition)
    {
        return pointerPosition.X >= Position.X
            && pointerPosition.X < Position.X + Size.Width
            && pointerPosition.Y >= Position.Y
            && pointerPosition.Y < Position.Y + 18;
    }

    private int GetNextUIElementId()
    {
        return nextUIElementId++;
    }
}