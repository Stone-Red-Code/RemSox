using Cosmos.Kernel.System.Input;

using RemSox.Kernel.Utils;
using RemSox.Shared.UI;
using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

/// <summary>
/// A single line of editable text, for names and other short input. It takes the window's keys
/// while focused (clicked, or given <see cref="Windows.Window.Focus"/>): typing inserts at the
/// cursor, Backspace and Delete remove, Left, Right, Home and End move. Enter raises
/// <see cref="Submitted"/> and Escape <see cref="Cancelled"/>; what happens then is the owner's
/// choice.
/// </summary>
/// <remarks>
/// The text is drawn in one row of cells as tall as the box allows. Text longer than fits scrolls
/// sideways to keep the cursor in view.
/// </remarks>
public sealed class TextBox() : Control("TextBox")
{
    private const int Padding = 4;

    private static readonly Color FocusedBorder = Color.FromArgb(0, 120, 215);
    private static readonly Color UnfocusedBorder = Color.FromArgb(100, 100, 100);

    // The first character shown, when the text is wider than the box.
    private int scroll;

    /// <summary>Raised with the text when the user presses Enter.</summary>
    public event Action<string>? Submitted;

    /// <summary>Raised when the user presses Escape.</summary>
    public event Action? Cancelled;

    /// <summary>Gets or sets the text; setting it puts the cursor at its end.</summary>
    public string Text
    {
        get;
        set
        {
            SetProperty(nameof(Text), ref field, value);
            CursorPosition = value.Length;
        }
    } = string.Empty;

    /// <summary>Gets or sets where the cursor is, as the number of characters before it.</summary>
    public int CursorPosition
    {
        get;
        set => SetProperty(nameof(CursorPosition), ref field, Math.Clamp(value, 0, Text.Length));
    }

    /// <summary>Gets or sets the longest text the box accepts.</summary>
    public int MaxLength { get; set; } = 255;

    public Color TextColor
    {
        get;
        set => SetProperty(nameof(TextColor), ref field, value);
    } = Color.White;

    private int FontSize => Math.Max(8, Size.Height - (2 * Padding) + 2);

    private int Columns => Math.Max(1, (Size.Width - (2 * Padding)) / Math.Max(1, CanvasExtensions.GetCellWidth(FontSize)));

    public override IEnumerable<RenderCommand> ToPrimitives(int windowId)
    {
        if (Size.Width <= 0 || Size.Height <= 0)
        {
            yield break;
        }

        int columns = Columns;
        int cellWidth = CanvasExtensions.GetCellWidth(FontSize);

        // Scroll just enough to keep the cursor in view (it may sit after the last character).
        scroll = Math.Clamp(scroll, Math.Max(0, CursorPosition - columns + 1), CursorPosition);
        string shown = Text.Length > scroll ? Text.Substring(scroll, Math.Min(columns, Text.Length - scroll)) : string.Empty;

        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Type = RenderCommandType.DrawFilledRect,
            Position = Position,
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = BackgroundColor,
                ["Size"] = Size,
            }
        };

        yield return new RenderCommand
        {
            WindowId = windowId,
            ElementId = PrimitiveId(1),
            Type = RenderCommandType.DrawRectBorder,
            Position = Position,
            Properties = new Dictionary<string, object?>
            {
                ["Color"] = IsFocused ? FocusedBorder : UnfocusedBorder,
                ["Size"] = Size,
            }
        };

        if (shown.Length > 0)
        {
            yield return new RenderCommand
            {
                WindowId = windowId,
                ElementId = PrimitiveId(2),
                Type = RenderCommandType.DrawText,
                Position = new Point(Position.X + Padding, Position.Y + Padding - 1),
                Properties = new Dictionary<string, object?>
                {
                    ["Color"] = TextColor,
                    ["Content"] = shown,
                    ["FontSize"] = FontSize,
                    ["MaxWidth"] = Size.Width - (2 * Padding),
                }
            };
        }

        // The cursor: a thin bar before the character it is at, only while the box has the keys.
        if (IsFocused)
        {
            yield return new RenderCommand
            {
                WindowId = windowId,
                ElementId = PrimitiveId(3),
                Type = RenderCommandType.DrawFilledRect,
                Position = new Point(Position.X + Padding + ((CursorPosition - scroll) * cellWidth), Position.Y + Padding - 1),
                Properties = new Dictionary<string, object?>
                {
                    ["Color"] = TextColor,
                    ["Size"] = new Size(1, Size.Height - (2 * Padding) + 2),
                }
            };
        }
    }

    public override void HandleKeyEvent(KeyEvent keyEvent)
    {
        string text = Text;
        int cursor = CursorPosition;

        switch (keyEvent.Key)
        {
            case Key.Enter or Key.NumEnter:
                Submitted?.Invoke(text);
                return;

            case Key.Escape:
                Cancelled?.Invoke();
                return;

            case Key.LeftArrow:
                CursorPosition = cursor - 1;
                return;

            case Key.RightArrow:
                CursorPosition = cursor + 1;
                return;

            case Key.Home:
                CursorPosition = 0;
                return;

            case Key.End:
                CursorPosition = text.Length;
                return;

            case Key.Backspace:
                if (cursor > 0)
                {
                    SetText(text.Remove(cursor - 1, 1), cursor - 1);
                }
                return;

            case Key.Delete:
                if (cursor < text.Length)
                {
                    SetText(text.Remove(cursor, 1), cursor);
                }
                return;
        }

        // Ctrl and Alt combinations are no text.
        char c = keyEvent.KeyChar;
        if (!char.IsControl(c) && c != '\0' && text.Length < MaxLength
            && !keyEvent.Modifiers.HasFlag(ConsoleModifiers.Control) && !keyEvent.Modifiers.HasFlag(ConsoleModifiers.Alt))
        {
            SetText(text.Insert(cursor, c.ToString()), cursor + 1);
        }
    }

    public override void HandleMouseEvent(MouseEvent mouseEvent)
    {
        // A click puts the cursor at the character clicked (the window focuses the box itself).
        if (mouseEvent.Type == MouseEventType.ButtonDown && mouseEvent.Button == MouseButton.Left)
        {
            int cellWidth = Math.Max(1, CanvasExtensions.GetCellWidth(FontSize));
            CursorPosition = scroll + (((mouseEvent.X - Position.X - Padding) + (cellWidth / 2)) / cellWidth);
        }
    }

    private void SetText(string text, int cursor)
    {
        // The setter puts the cursor at the end; then it goes where the edit left it.
        Text = text;
        CursorPosition = cursor;
    }
}
