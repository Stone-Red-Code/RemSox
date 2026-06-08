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

    public bool AutoFlush { get; set; } = false;

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

    public bool IsDragging => isDragging;

    private readonly Dictionary<int, UIElement> uiElements = [];

    private int nextUIElementId = 1;

    private bool isDragging;

    private Point dragOffset;

    private Point lastRenderedPosition = new Point(-1, -1);
    private Size lastRenderedSize = new Size(-1, -1);
    private bool lastRenderedIsFocused = false;
    private string lastRenderedTitle = string.Empty;
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

    public void Flush()
    {
        if (!IsVisible)
        {
            return;
        }

        bool windowStateChanged = isFirstRender || Size != lastRenderedSize || IsFocused != lastRenderedIsFocused || Title != lastRenderedTitle;
        bool anyChildChanged = uiElements.Values.Any(e => e.AnyPropertyChanged);
        bool positionChanged = Position != lastRenderedPosition;

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
                    [nameof(IsDraggable)] = IsDraggable
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
        else if (positionChanged)
        {
            commands.Add(new RenderCommand
            {
                WindowId = Id,
                ElementId = Id,
                ElementType = "WindowMove",
                Position = Position,
                Properties = new Dictionary<string, object?>()
            });
        }

        if (commands.Count > 0)
        {
            renderSource.Render(commands);
            lastRenderedPosition = Position;
            lastRenderedSize = Size;
            lastRenderedIsFocused = IsFocused;
            lastRenderedTitle = Title;
            isFirstRender = false;
        }
    }

    public bool TryBeginDrag(Point pointerPosition)
    {
        if (!IsVisible || !IsDraggable || !IsPointInTitleBar(pointerPosition))
        {
            return false;
        }

        isDragging = true;
        dragOffset = new Point(pointerPosition.X - Position.X, pointerPosition.Y - Position.Y);

        WindowManager.FocusWindow(this);

        return true;
    }

    public void DragTo(Point pointerPosition)
    {
        if (!isDragging || !IsDraggable)
        {
            return;
        }

        int newX = pointerPosition.X - dragOffset.X;
        int newY = pointerPosition.Y - dragOffset.Y;

        // Cosmos DrawCanvas fails to render if coordinates are negative.
        // Clamp to 0,0 to prevent the window from disappearing.
        newX = Math.Max(0, newX);
        newY = Math.Max(0, newY);

        Position = new Point(newX, newY);

        Flush();
    }

    public void EndDrag()
    {
        isDragging = false;
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