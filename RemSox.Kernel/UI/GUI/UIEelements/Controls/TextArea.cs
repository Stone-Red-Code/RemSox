using Cosmos.Kernel.System.Input;

using RemSox.Kernel.Utils;
using RemSox.Shared.UI;
using RemSox.Shared.UI.GUI.Rendering;

using System.Drawing;

namespace RemSox.Kernel.UI.GUI.UIEelements.Controls;

/// <summary>
/// Several lines of editable text, for a text editor. While focused it takes the window's keys:
/// typing inserts at the cursor, Enter splits the line, Backspace and Delete join lines at their
/// ends, the arrows, Home, End, Page Up and Page Down move (with Ctrl, Home and End go to the
/// start and end of the text), Tab inserts spaces. A click puts the cursor where it lands and
/// the wheel scrolls.
/// </summary>
/// <remarks>
/// Only the lines in view are drawn, each clipped to the area inside the border, so a file of
/// any length costs the same to show; lines wider than the area scroll sideways with the
/// cursor. Give it a <see cref="ScrollBar"/> (made by the owner in the same window) to show
/// and move the vertical position. Changes to the text raise <see cref="TextChanged"/>.
/// </remarks>
public sealed class TextArea() : Control("TextArea")
{
    private const int Padding = 4;
    private const int TabWidth = 4;

    // Each line is a primitive of the element, which has 64: the frame and cursor take 3.
    private const int MaxRows = 60;

    private static readonly Color FocusedBorder = Color.FromArgb(0, 120, 215);
    private static readonly Color UnfocusedBorder = Color.FromArgb(100, 100, 100);

    private List<string> lines = [string.Empty];
    private int wantedColumn;
    private ScrollBar? scrollBar;

    public override CursorRole Cursor => CursorRole.Text;

    /// <summary>Raised after the user changed the text.</summary>
    public event Action? TextChanged;

    /// <summary>Gets or sets the whole text, lines separated by <c>\n</c>; setting it puts the cursor at the start.</summary>
    public string Text
    {
        get => string.Join('\n', lines);
        set => SetLines(value.Replace("\r\n", "\n").Split('\n'));
    }

    /// <summary>Gets the lines of the text; there is always at least one.</summary>
    public IReadOnlyList<string> Lines => lines;

    /// <summary>Gets the line the cursor is on, from 0.</summary>
    public int CursorLine { get; private set; }

    /// <summary>Gets the cursor's place in its line: how many characters come before it.</summary>
    public int CursorColumn { get; private set; }

    /// <summary>Gets the first line in view.</summary>
    public int TopLine { get; private set; }

    /// <summary>Gets the first column in view, when lines are wider than the area.</summary>
    public int LeftColumn { get; private set; }

    public int FontSize
    {
        get;
        set => SetProperty(nameof(FontSize), ref field, Math.Clamp(value, 8, 64));
    } = 16;

    public Color TextColor
    {
        get;
        set => SetProperty(nameof(TextColor), ref field, value);
    } = Color.FromArgb(230, 230, 230);

    // Bumped on every change the other properties do not show (an edit, a cursor move, a
    // scroll), so the window draws the area again.
    private int Revision
    {
        get;
        set => SetProperty(nameof(Revision), ref field, value);
    }

    private int LineHeight => FontSize + 2;

    private int CellWidth => Math.Max(1, CanvasExtensions.GetCellWidth(FontSize));

    /// <summary>Gets how many lines fit in the area.</summary>
    public int VisibleRows => Math.Clamp((Size.Height - (2 * Padding)) / LineHeight, 1, MaxRows);

    private int VisibleColumns => Math.Max(1, (Size.Width - (2 * Padding)) / CellWidth);

    private Rectangle Inner => new(Position.X + Padding, Position.Y + Padding, Math.Max(0, Size.Width - (2 * Padding)), Math.Max(0, Size.Height - (2 * Padding)));

    /// <summary>
    /// Gets or sets the scroll bar that shows and moves which lines are in view. The owner
    /// makes it in the area's window and places it; the area keeps its range and value.
    /// </summary>
    public ScrollBar? ScrollBar
    {
        get => scrollBar;
        set
        {
            if (scrollBar is not null)
            {
                scrollBar.Scrolled -= ScrollTo;
            }

            scrollBar = value;
            if (scrollBar is not null)
            {
                scrollBar.Scrolled += ScrollTo;
            }

            UpdateScrollBar();
        }
    }

    /// <summary>Replaces the text with <paramref name="newLines"/> (tabs become spaces) and puts the cursor at the start.</summary>
    public void SetLines(IEnumerable<string> newLines)
    {
        lines = [.. newLines.Select(line => line.Replace("\t", new string(' ', TabWidth)))];
        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        CursorLine = 0;
        CursorColumn = 0;
        TopLine = 0;
        LeftColumn = 0;
        wantedColumn = 0;
        Changed(scroll: false);
    }

    /// <summary>Scrolls so <paramref name="line"/> is the first in view (kept in range); the cursor stays where it is.</summary>
    public void ScrollTo(int line)
    {
        int top = Math.Clamp(line, 0, Math.Max(0, lines.Count - VisibleRows));
        if (top != TopLine)
        {
            TopLine = top;
            Changed(scroll: false);
        }
    }

    public override IEnumerable<Primitive> ToPrimitives(int windowId)
    {
        if (Size.Width <= 0 || Size.Height <= 0)
        {
            yield break;
        }

        Rectangle inner = Inner;
        int rows = VisibleRows;
        int columns = VisibleColumns;

        yield return new FilledRectangle
        {
            WindowId = windowId,
            ElementId = PrimitiveId(0),
            Position = Position,
            Color = BackgroundColor,
            Size = Size,
        };

        yield return new RectangleBorder
        {
            WindowId = windowId,
            ElementId = PrimitiveId(1),
            Position = Position,
            Color = IsFocused ? FocusedBorder : UnfocusedBorder,
            Size = Size,
        };

        for (int row = 0; row < rows && TopLine + row < lines.Count; row++)
        {
            string line = lines[TopLine + row];
            if (line.Length <= LeftColumn)
            {
                continue;
            }

            // One column more than fits, cut by the clip, so a line that goes on shows it does.
            string shown = line.Substring(LeftColumn, Math.Min(columns + 1, line.Length - LeftColumn));
            yield return new TextPrimitive
            {
                WindowId = windowId,
                ElementId = PrimitiveId(3 + row),
                Position = new Point(inner.X, inner.Y + (row * LineHeight)),
                Color = TextColor,
                Content = shown,
                FontSize = FontSize,
                Clip = inner,
            };
        }

        int cursorRow = CursorLine - TopLine;
        if (IsFocused && cursorRow >= 0 && cursorRow < rows)
        {
            yield return new FilledRectangle
            {
                WindowId = windowId,
                ElementId = PrimitiveId(2),
                Position = new Point(inner.X + ((CursorColumn - LeftColumn) * CellWidth), inner.Y + (cursorRow * LineHeight)),
                Color = TextColor,
                Size = new Size(1, LineHeight),
                Clip = inner,
            };
        }
    }

    public override void HandleKeyEvent(KeyEvent keyEvent)
    {
        bool control = keyEvent.Modifiers.HasFlag(ConsoleModifiers.Control);
        string line = lines[CursorLine];

        switch (keyEvent.Key)
        {
            case Key.LeftArrow:
                if (CursorColumn > 0)
                {
                    MoveTo(CursorLine, CursorColumn - 1);
                }
                else if (CursorLine > 0)
                {
                    MoveTo(CursorLine - 1, lines[CursorLine - 1].Length);
                }
                return;

            case Key.RightArrow:
                if (CursorColumn < line.Length)
                {
                    MoveTo(CursorLine, CursorColumn + 1);
                }
                else if (CursorLine < lines.Count - 1)
                {
                    MoveTo(CursorLine + 1, 0);
                }
                return;

            case Key.UpArrow:
                MoveVertically(-1);
                return;

            case Key.DownArrow:
                MoveVertically(1);
                return;

            case Key.PageUp:
                MoveVertically(-VisibleRows);
                return;

            case Key.PageDown:
                MoveVertically(VisibleRows);
                return;

            case Key.Home:
                MoveTo(control ? 0 : CursorLine, 0);
                return;

            case Key.End:
                MoveTo(control ? lines.Count - 1 : CursorLine, control ? lines[^1].Length : line.Length);
                return;

            case Key.Enter or Key.NumEnter:
                lines[CursorLine] = line[..CursorColumn];
                lines.Insert(CursorLine + 1, line[CursorColumn..]);
                Edited(CursorLine + 1, 0);
                return;

            case Key.Backspace:
                if (CursorColumn > 0)
                {
                    lines[CursorLine] = line.Remove(CursorColumn - 1, 1);
                    Edited(CursorLine, CursorColumn - 1);
                }
                else if (CursorLine > 0)
                {
                    int joinAt = lines[CursorLine - 1].Length;
                    lines[CursorLine - 1] += line;
                    lines.RemoveAt(CursorLine);
                    Edited(CursorLine - 1, joinAt);
                }
                return;

            case Key.Delete:
                if (CursorColumn < line.Length)
                {
                    lines[CursorLine] = line.Remove(CursorColumn, 1);
                    Edited(CursorLine, CursorColumn);
                }
                else if (CursorLine < lines.Count - 1)
                {
                    lines[CursorLine] = line + lines[CursorLine + 1];
                    lines.RemoveAt(CursorLine + 1);
                    Edited(CursorLine, CursorColumn);
                }
                return;

            case Key.Tab:
                int spaces = TabWidth - (CursorColumn % TabWidth);
                lines[CursorLine] = line.Insert(CursorColumn, new string(' ', spaces));
                Edited(CursorLine, CursorColumn + spaces);
                return;
        }

        // Ctrl and Alt combinations are no text: the owner may use them (Ctrl+S).
        char c = keyEvent.KeyChar;
        if (!char.IsControl(c) && c != '\0' && !control && !keyEvent.Modifiers.HasFlag(ConsoleModifiers.Alt))
        {
            lines[CursorLine] = line.Insert(CursorColumn, c.ToString());
            Edited(CursorLine, CursorColumn + 1);
        }
    }

    public override void HandleMouseEvent(MouseEvent mouseEvent)
    {
        switch (mouseEvent.Type)
        {
            case MouseEventType.ButtonDown when mouseEvent.Button == MouseButton.Left:
                Rectangle inner = Inner;
                int row = Math.Max(0, (mouseEvent.Y - inner.Y) / LineHeight);
                int column = Math.Max(0, ((mouseEvent.X - inner.X) + (CellWidth / 2)) / CellWidth);
                MoveTo(Math.Min(TopLine + row, lines.Count - 1), LeftColumn + column);
                break;

            case MouseEventType.Wheel:
                ScrollTo(TopLine + (mouseEvent.Delta * 3));
                break;
        }
    }

    private void MoveVertically(int rows)
    {
        int target = Math.Clamp(CursorLine + rows, 0, lines.Count - 1);
        int column = Math.Min(wantedColumn, lines[target].Length);
        MoveTo(target, column, keepWanted: true);
    }

    /// <summary>Puts the cursor at <paramref name="line"/> and <paramref name="column"/> (kept in the text) and brings it into view.</summary>
    private void MoveTo(int line, int column, bool keepWanted = false)
    {
        CursorLine = Math.Clamp(line, 0, lines.Count - 1);
        CursorColumn = Math.Clamp(column, 0, lines[CursorLine].Length);
        if (!keepWanted)
        {
            wantedColumn = CursorColumn;
        }

        Changed(scroll: true);
    }

    private void Edited(int line, int column)
    {
        MoveTo(line, column);
        TextChanged?.Invoke();
    }

    /// <summary>Scrolls to the cursor when asked, keeps the bar in step and has the area drawn again.</summary>
    private void Changed(bool scroll)
    {
        if (scroll)
        {
            int rows = VisibleRows;
            if (CursorLine < TopLine)
            {
                TopLine = CursorLine;
            }
            else if (CursorLine >= TopLine + rows)
            {
                TopLine = CursorLine - rows + 1;
            }

            int columns = VisibleColumns;
            if (CursorColumn < LeftColumn)
            {
                LeftColumn = Math.Max(0, CursorColumn - (columns / 4));
            }
            else if (CursorColumn > LeftColumn + columns - 1)
            {
                LeftColumn = CursorColumn - columns + 1;
            }
        }

        TopLine = Math.Clamp(TopLine, 0, Math.Max(0, lines.Count - 1));
        UpdateScrollBar();
        Revision++;
    }

    private void UpdateScrollBar()
    {
        if (scrollBar is not null)
        {
            scrollBar.Maximum = lines.Count;
            scrollBar.ViewportSize = VisibleRows;
            scrollBar.Value = TopLine;
        }
    }
}
