using System;
using System.Drawing;
using System.Reflection;
using RemSox.UI.GUI.Rendering;
using RemSox.UI.GUI.UIEelements;

namespace RemSox.UI.GUI.Windows;

public sealed class Window(string title, int processId, int id, IRenderSource renderSource)
{
    public int Id { get; } = id;

    public int ProcessId { get; } = processId;

    public string Title { get; set; } = title;

    public int ZIndex { get; set; }

    public bool AutoFlush { get; set; } = false;

    public event Action<Cosmos.Kernel.System.Keyboard.KeyEvent>? OnKeyEvent;

    public void HandleKeyEvent(Cosmos.Kernel.System.Keyboard.KeyEvent keyEvent)
    {
        OnKeyEvent?.Invoke(keyEvent);
    }

    public bool IsFocused
    {
        get => WindowManager.IsWindowFocused(this);
        set
        {
            WindowManager.FocusWindow(value ? this : null);
        }
    }

    public bool IsVisible { get; set; } = true;

    public bool IsResizable { get; set; } = true;

    public bool IsDraggable { get; set; } = true;

    public Point Position { get; set; }

    public Size Size { get; set; }

    public bool IsDragging => currentInteraction == InteractionMode.Drag;

    private readonly Dictionary<int, UIElement> uiElements = [];

    private int nextUIElementId = 1;

    private enum InteractionMode { None, Drag, ResizeTop, ResizeBottom, ResizeLeft, ResizeRight, ResizeTopLeft, ResizeTopRight, ResizeBottomLeft, ResizeBottomRight }
    private InteractionMode currentInteraction = InteractionMode.None;
    private Rectangle interactionStartBounds;
    private Point interactionStartPointer;
    private Point dragOffset;

    private Point lastRenderedPosition = new Point(-1, -1);
    private Size lastRenderedSize = new Size(-1, -1);
    private bool lastRenderedIsFocused = false;
    private string lastRenderedTitle = string.Empty;
    private int lastRenderedZIndex = -1;
    private bool isFirstRender = true;

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

        uiElements.Add(uiElementId, uiElement);

        return uiElement;
    }

    public void Invalidate()
    {
        isFirstRender = true;
        Flush();
    }

    public void Flush()
    {
        if (!IsVisible)
        {
            return;
        }

        bool windowStateChanged = isFirstRender || Size != lastRenderedSize || IsFocused != lastRenderedIsFocused || Title != lastRenderedTitle;
        bool anyChildChanged = uiElements.Values.Any(e => e.AnyPropertyChanged);
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

            foreach (UIElement element in uiElements.Values)
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
            if (onTop && onLeft) currentInteraction = InteractionMode.ResizeTopLeft;
            else if (onTop && onRight) currentInteraction = InteractionMode.ResizeTopRight;
            else if (onBottom && onLeft) currentInteraction = InteractionMode.ResizeBottomLeft;
            else if (onBottom && onRight) currentInteraction = InteractionMode.ResizeBottomRight;
            else if (onLeft && inBounds) currentInteraction = InteractionMode.ResizeLeft;
            else if (onRight && inBounds) currentInteraction = InteractionMode.ResizeRight;
            else if (onTop && inBounds) currentInteraction = InteractionMode.ResizeTop;
            else if (onBottom && inBounds) currentInteraction = InteractionMode.ResizeBottom;
        }

        if (currentInteraction == InteractionMode.None && IsDraggable && IsPointInTitleBar(pointerPosition))
        {
            currentInteraction = InteractionMode.Drag;
            dragOffset = new Point(pointerPosition.X - Position.X, pointerPosition.Y - Position.Y);
        }

        if (currentInteraction != InteractionMode.None)
        {
            interactionStartBounds = new Rectangle(Position, Size);
            interactionStartPointer = pointerPosition;
            WindowManager.FocusWindow(this);
            return true;
        }

        return false;
    }

    public void UpdateInteraction(Point pointerPosition)
    {
        if (currentInteraction == InteractionMode.Drag)
        {
            int newX = pointerPosition.X - dragOffset.X;
            int newY = pointerPosition.Y - dragOffset.Y;

            // Cosmos DrawCanvas fails to render if coordinates are negative.
            // Clamp to 0,0 to prevent the window from disappearing.
            newX = Math.Max(0, newX);
            newY = Math.Max(0, newY);

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

            if (currentInteraction == InteractionMode.ResizeRight || currentInteraction == InteractionMode.ResizeBottomRight || currentInteraction == InteractionMode.ResizeTopRight)
            {
                newW = Math.Max(minWidth, interactionStartBounds.Width + dx);
            }
            if (currentInteraction == InteractionMode.ResizeBottom || currentInteraction == InteractionMode.ResizeBottomRight || currentInteraction == InteractionMode.ResizeBottomLeft)
            {
                newH = Math.Max(minHeight, interactionStartBounds.Height + dy);
            }
            if (currentInteraction == InteractionMode.ResizeLeft || currentInteraction == InteractionMode.ResizeBottomLeft || currentInteraction == InteractionMode.ResizeTopLeft)
            {
                int maxDx = interactionStartBounds.Width - minWidth;
                int clampedDx = Math.Min(dx, maxDx);
                newX = Math.Max(0, interactionStartBounds.X + clampedDx);
                newW = interactionStartBounds.Width - clampedDx;
            }
            if (currentInteraction == InteractionMode.ResizeTop || currentInteraction == InteractionMode.ResizeTopLeft || currentInteraction == InteractionMode.ResizeTopRight)
            {
                int maxDy = interactionStartBounds.Height - minHeight;
                int clampedDy = Math.Min(dy, maxDy);
                newY = Math.Max(0, interactionStartBounds.Y + clampedDy);
                newH = interactionStartBounds.Height - clampedDy;
            }

            Position = new Point(newX, newY);
            Size = new Size(newW, newH);
            Flush();
        }
    }

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