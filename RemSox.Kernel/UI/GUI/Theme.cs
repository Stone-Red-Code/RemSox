using System.Drawing;
using System.Globalization;

using RemSox.Kernel.Configuration;

namespace RemSox.Kernel.UI.GUI;

/// <summary>
/// The colours and corner radii a screen is drawn with: one colour per <see cref="ThemeRole"/>
/// and two radii. Built-in themes (<see cref="Dark"/>, <see cref="Light"/>) are defined here; a
/// theme file (<c>key = value</c>, <see cref="Parse"/>) changes some of a base theme's values.
/// Finding and reading the files is <c>Themes</c>' job (design: <c>docs/design/theming.md</c>).
/// </summary>
/// <remarks>
/// Sizes are not part of a theme: programs lay their windows out with constants such as
/// <c>Window.TitleBarHeight</c>, so they are the same in every theme.
/// </remarks>
public sealed class Theme
{
    /// <summary>The built-in dark theme's name, the default <c>theme</c> setting and the base of a file that names none.</summary>
    public const string DarkName = "dark";

    /// <summary>The built-in light theme's name.</summary>
    public const string LightName = "light";

    /// <summary>The key of a theme file naming the theme whose values fill in what the file leaves out.</summary>
    public const string BaseKey = "base";

    /// <summary>The key of the corner radius of controls, fields and list rows.</summary>
    public const string RadiusKey = "radius";

    /// <summary>The key of the corner radius of windows and the start menu.</summary>
    public const string WindowRadiusKey = "window.radius";

    /// <summary>The largest corner radius a theme may ask for.</summary>
    public const int MaxRadius = 16;

    /// <summary>A theme file's extension.</summary>
    public const string FileExtension = ".theme";

    // Each role's key in a theme file, in the order of ThemeRole (whose values index colors).
    private static readonly string[] RoleKeys =
    [
        "background", "surface", "field", "control", "control.hover", "control.pressed",
        "border", "border.focus", "text", "text.muted", "text.disabled",
        "accent", "accent.hover", "text.on-accent", "selection", "danger",
        "terminal.background", "terminal.text", "terminal.error",
    ];

    private readonly Color[] colors;

    private Theme(string name, Color[] colors, int radius, int windowRadius)
    {
        Name = name;
        this.colors = colors;
        Radius = radius;
        WindowRadius = windowRadius;
    }

    /// <summary>Gets the built-in dark theme, the default: near-black greys, light text, a blue accent.</summary>
    public static Theme Dark { get; } = Define(DarkName, radius: 4, windowRadius: 8,
    [
        0xFF202020, 0xFF2B2B2B, 0xFF181818, 0xFF3A3A3A, 0xFF454545, 0xFF303030,
        0xFF555555, 0xFF3478F6, 0xFFE6E6E6, 0xFFA0A0A0, 0xFF6E6E6E,
        0xFF3478F6, 0xFF4A88F7, 0xFFFFFFFF, 0xFF264F78, 0xFFE5534B,
        0xFF0C0C0C, 0xFF90EE90, 0xFFFF6E64,
    ]);

    /// <summary>Gets the built-in light theme: white and light greys, dark text, a blue accent.</summary>
    public static Theme Light { get; } = Define(LightName, radius: 4, windowRadius: 8,
    [
        0xFFF3F3F3, 0xFFE6E6E6, 0xFFFFFFFF, 0xFFDDDDDD, 0xFFD2D2D2, 0xFFC4C4C4,
        0xFFB8B8B8, 0xFF0067C0, 0xFF1B1B1B, 0xFF5F5F5F, 0xFFA0A0A0,
        0xFF0067C0, 0xFF1975C5, 0xFFFFFFFF, 0xFFCCE4F7, 0xFFC42B1C,
        0xFFFFFFFF, 0xFF1B1B1B, 0xFFC42B1C,
    ]);

    /// <summary>Gets the built-in themes.</summary>
    public static IReadOnlyList<Theme> BuiltIn { get; } = [Dark, Light];

    /// <summary>Gets the theme's name: the built-in one's, or its file's without the extension.</summary>
    public string Name { get; }

    /// <summary>Gets the corner radius of controls, fields and list rows.</summary>
    public int Radius { get; }

    /// <summary>Gets the corner radius of windows and the start menu.</summary>
    public int WindowRadius { get; }

    /// <summary>Gets the colour of <paramref name="role"/>.</summary>
    public Color this[ThemeRole role] => colors[(int)role];

    /// <summary>Returns the built-in theme called <paramref name="name"/>, or null.</summary>
    public static Theme? GetBuiltIn(string name)
    {
        foreach (Theme theme in BuiltIn)
        {
            if (string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return theme;
            }
        }

        return null;
    }

    /// <summary>Returns why <paramref name="name"/> cannot name a theme file, or null when it can.</summary>
    public static string? ValidateName(string name)
    {
        return name.Length > 0 && name.Length <= 32 && name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_')
            ? null
            : "a theme name: up to 32 lower-case letters, digits, - and _";
    }

    /// <summary>
    /// Returns the base a theme file names (<c>base = ...</c>), or null when it names none: the
    /// built-in theme of the file's own name then, else <see cref="DarkName"/>.
    /// </summary>
    public static string? GetBaseName(KeyValueFile file)
    {
        return file.Get(BaseKey) is { Length: > 0 } name ? name.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Reads a theme file: its values over <paramref name="baseTheme"/>'s. A line that is not a
    /// role, a radius or the base, or whose value is not valid, is added to
    /// <paramref name="problems"/> and the base's value kept.
    /// </summary>
    public static Theme Parse(string name, KeyValueFile file, Theme baseTheme, List<string> problems)
    {
        Color[] colors = [.. baseTheme.colors];
        int radius = baseTheme.Radius;
        int windowRadius = baseTheme.WindowRadius;

        foreach ((string key, string value) in file.Entries)
        {
            if (key == BaseKey)
            {
                continue;
            }

            if (key is RadiusKey or WindowRadiusKey)
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed > MaxRadius)
                {
                    problems.Add($"{key}: '{value}' is not a radius from 0 to {MaxRadius}");
                }
                else if (key == RadiusKey)
                {
                    radius = parsed;
                }
                else
                {
                    windowRadius = parsed;
                }

                continue;
            }

            int index = Array.IndexOf(RoleKeys, key);
            if (index < 0)
            {
                problems.Add($"unknown key '{key}' ignored");
            }
            else if (!TryParseColor(value, out Color color))
            {
                problems.Add($"{key}: '{value}' is not a colour (#RRGGBB or #AARRGGBB)");
            }
            else
            {
                colors[index] = color;
            }
        }

        return new Theme(name, colors, radius, windowRadius);
    }

    /// <summary>
    /// Returns the theme with <paramref name="accent"/> as its accent (the <c>accent</c>
    /// setting): the accent, a lighter accent under the pointer, the focus frame, and a
    /// selection mixed from the accent and the background.
    /// </summary>
    public Theme WithAccent(Color accent)
    {
        Color[] changed = [.. colors];
        Color background = this[ThemeRole.Background];
        changed[(int)ThemeRole.Accent] = accent;
        changed[(int)ThemeRole.AccentHover] = Mix(accent, Color.White, 0.15);
        changed[(int)ThemeRole.BorderFocus] = accent;
        changed[(int)ThemeRole.Selection] = Mix(accent, background, IsDark(background) ? 0.55 : 0.8);
        return new Theme(Name, changed, Radius, WindowRadius);
    }

    /// <summary>Returns whether the two themes draw everything the same, so a screen need not draw again.</summary>
    public bool LooksLike(Theme other)
    {
        return Radius == other.Radius && WindowRadius == other.WindowRadius
            && colors.Select(c => c.ToArgb()).SequenceEqual(other.colors.Select(c => c.ToArgb()));
    }

    /// <summary>
    /// Returns the lines of a theme file setting every value of this theme, as the built-in
    /// themes are written to the system's themes folder: from then on that file is the theme,
    /// and changing a line changes it for everyone using it.
    /// </summary>
    public IEnumerable<string> ToLines()
    {
        yield return $"# The {Name} theme, for everyone using '{Name}'. Change a value to change it;";
        yield return "# delete the file to have RemSox write the built-in one again at boot.";
        yield return "# Colours are #RRGGBB or #AARRGGBB.";
        yield return $"{RadiusKey} = {Radius}";
        yield return $"{WindowRadiusKey} = {WindowRadius}";
        for (int i = 0; i < RoleKeys.Length; i++)
        {
            yield return $"{RoleKeys[i]} = {FormatColor(colors[i])}";
        }
    }

    /// <summary>Reads <c>#RRGGBB</c> or <c>#AARRGGBB</c>.</summary>
    public static bool TryParseColor(string value, out Color color)
    {
        color = Color.Empty;
        if (value.Length is not (7 or 9) || value[0] != '#'
            || !uint.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint argb))
        {
            return false;
        }

        color = Color.FromArgb((int)(value.Length == 7 ? 0xFF000000u | argb : argb));
        return true;
    }

    /// <summary>Writes <c>#RRGGBB</c>, or <c>#AARRGGBB</c> for a colour that is not opaque.</summary>
    public static string FormatColor(Color color)
    {
        uint argb = (uint)color.ToArgb();
        return color.A == 255
            ? "#" + (argb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture)
            : "#" + argb.ToString("X8", CultureInfo.InvariantCulture);
    }

    private static Theme Define(string name, int radius, int windowRadius, uint[] argb)
    {
        if (argb.Length != RoleKeys.Length)
        {
            throw new ArgumentException($"The {name} theme has {argb.Length} colours for {RoleKeys.Length} roles.", nameof(argb));
        }

        return new Theme(name, [.. argb.Select(value => Color.FromArgb((int)value))], radius, windowRadius);
    }

    /// <summary>
    /// Returns the colour a control of <paramref name="background"/> shows under the pointer,
    /// or held down: the theme's own hover and pressed colours for <see cref="ThemeRole.Control"/>
    /// and <see cref="ThemeRole.Accent"/>, else the colour moved a little towards the text
    /// (hover) or the window's background (pressed).
    /// </summary>
    public Color GetInteractive(ThemeColor background, bool hovered, bool pressed)
    {
        Color color = background.Resolve(this);
        if (pressed && hovered)
        {
            return background == ThemeRole.Control ? this[ThemeRole.ControlPressed] : Mix(color, this[ThemeRole.Background], 0.25);
        }

        if (hovered)
        {
            return background == ThemeRole.Control ? this[ThemeRole.ControlHover]
                : background == ThemeRole.Accent ? this[ThemeRole.AccentHover]
                : Mix(color, this[ThemeRole.Text], 0.12);
        }

        return color;
    }

    /// <summary>Returns <paramref name="color"/> moved by <paramref name="otherShare"/> (0-1) towards <paramref name="other"/>, keeping its alpha.</summary>
    public static Color Mix(Color color, Color other, double otherShare)
    {
        return Color.FromArgb(
            color.A,
            (int)Math.Round((color.R * (1 - otherShare)) + (other.R * otherShare)),
            (int)Math.Round((color.G * (1 - otherShare)) + (other.G * otherShare)),
            (int)Math.Round((color.B * (1 - otherShare)) + (other.B * otherShare)));
    }

    private static bool IsDark(Color color)
    {
        return (color.R * 299) + (color.G * 587) + (color.B * 114) < 128 * 1000;
    }
}
