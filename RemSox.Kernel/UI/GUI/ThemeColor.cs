using System.Drawing;

namespace RemSox.Kernel.UI.GUI;

/// <summary>
/// The colour of part of an element: a role of the theme (<see cref="ThemeRole"/>), the
/// default for chrome, or a fixed colour, for colours that are content (a terminal's error
/// line, a drawing). Either converts to it implicitly, so <c>text.Color = ThemeRole.TextMuted</c>
/// and <c>text.Color = Color.Red</c> both work. The element turns it into a colour when it is
/// drawn, so a role follows the theme when it changes.
/// </summary>
public readonly struct ThemeColor : IEquatable<ThemeColor>
{
    private readonly Color color;
    private readonly ThemeRole role;
    private readonly bool isRole;

    private ThemeColor(Color color, ThemeRole role, bool isRole)
    {
        this.color = color;
        this.role = role;
        this.isRole = isRole;
    }

    public static implicit operator ThemeColor(Color color)
    {
        return new ThemeColor(color, default, isRole: false);
    }

    public static implicit operator ThemeColor(ThemeRole role)
    {
        return new ThemeColor(default, role, isRole: true);
    }

    public static bool operator ==(ThemeColor left, ThemeColor right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ThemeColor left, ThemeColor right)
    {
        return !left.Equals(right);
    }

    /// <summary>Returns the colour to draw with in <paramref name="theme"/>.</summary>
    public Color Resolve(Theme theme)
    {
        return isRole ? theme[role] : color;
    }

    public bool Equals(ThemeColor other)
    {
        return isRole == other.isRole && (isRole ? role == other.role : color.ToArgb() == other.color.ToArgb());
    }

    public override bool Equals(object? obj)
    {
        return obj is ThemeColor other && Equals(other);
    }

    public override int GetHashCode()
    {
        return isRole ? (int)role : color.ToArgb();
    }

    public override string ToString()
    {
        return isRole ? role.ToString() : Theme.FormatColor(color);
    }
}
