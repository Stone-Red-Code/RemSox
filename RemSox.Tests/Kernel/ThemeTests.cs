using System.Drawing;

using RemSox.Kernel.Configuration;
using RemSox.Kernel.UI.GUI;

namespace RemSox.Tests.Kernel;

/// <summary>Themes: theme files over their base, the accent, and the colours elements name.</summary>
public sealed class ThemeTests
{
    [Fact]
    public void AFileChangesOnlyWhatItSets()
    {
        List<string> problems = [];
        KeyValueFile file = new(["# mine", "base = light", "accent = #112233", "radius = 6", "text.muted = #80AABBCC"]);

        Theme theme = Theme.Parse("mine", file, Theme.Light, problems);

        Assert.Empty(problems);
        Assert.Equal("mine", theme.Name);
        Assert.Equal(Color.FromArgb(0x11, 0x22, 0x33).ToArgb(), theme[ThemeRole.Accent].ToArgb());
        Assert.Equal(unchecked((int)0x80AABBCC), theme[ThemeRole.TextMuted].ToArgb());
        Assert.Equal(6, theme.Radius);
        Assert.Equal(Theme.Light.WindowRadius, theme.WindowRadius);
        Assert.Equal(Theme.Light[ThemeRole.Background].ToArgb(), theme[ThemeRole.Background].ToArgb());
        Assert.Equal("light", Theme.GetBaseName(file));
    }

    [Fact]
    public void BadLinesAreReportedAndKeepTheBase()
    {
        List<string> problems = [];
        KeyValueFile file = new(["accent = blue", "colour = #000000", "window.radius = 99", "text = #12345"]);

        Theme theme = Theme.Parse("broken", file, Theme.Dark, problems);

        Assert.Equal(4, problems.Count);
        Assert.True(theme.LooksLike(Theme.Dark));
        Assert.Null(Theme.GetBaseName(file));
    }

    [Fact]
    public void TheBuiltInFilesShowEveryValueAndSetNone()
    {
        foreach (Theme builtIn in Theme.BuiltIn)
        {
            List<string> lines = [.. builtIn.ToCommentedLines()];
            Assert.Empty(new KeyValueFile(lines).Entries);

            // Taking the # off every value line gives the theme back.
            KeyValueFile uncommented = new(lines.Where(l => l.Contains(" = ")).Select(l => l[2..]));
            List<string> problems = [];
            Assert.True(Theme.Parse(builtIn.Name, uncommented, Theme.Dark, problems).LooksLike(builtIn));
            Assert.Empty(problems);
        }
    }

    [Fact]
    public void TheAccentSettingRecoloursWhatFollowsIt()
    {
        Color accent = Color.FromArgb(0xE0, 0x53, 0x3D);

        Theme theme = Theme.Dark.WithAccent(accent);

        Assert.Equal(accent.ToArgb(), theme[ThemeRole.Accent].ToArgb());
        Assert.Equal(accent.ToArgb(), theme[ThemeRole.BorderFocus].ToArgb());
        Assert.NotEqual(Theme.Dark[ThemeRole.Selection].ToArgb(), theme[ThemeRole.Selection].ToArgb());
        Assert.Equal(Theme.Dark[ThemeRole.Text].ToArgb(), theme[ThemeRole.Text].ToArgb());
        Assert.False(theme.LooksLike(Theme.Dark));
    }

    [Fact]
    public void ControlsLightUpUnderThePointerAndDarkenWhenHeld()
    {
        Theme theme = Theme.Dark;

        Assert.Equal(theme[ThemeRole.ControlHover].ToArgb(), theme.GetInteractive(ThemeRole.Control, hovered: true, pressed: false).ToArgb());
        Assert.Equal(theme[ThemeRole.ControlPressed].ToArgb(), theme.GetInteractive(ThemeRole.Control, hovered: true, pressed: true).ToArgb());
        Assert.Equal(theme[ThemeRole.AccentHover].ToArgb(), theme.GetInteractive(ThemeRole.Accent, hovered: true, pressed: false).ToArgb());

        // Held, but the pointer moved off: shown at rest, as releasing there does nothing.
        Assert.Equal(theme[ThemeRole.Control].ToArgb(), theme.GetInteractive(ThemeRole.Control, hovered: false, pressed: true).ToArgb());

        // A colour of its own moves towards the text under the pointer, towards the background held.
        Color own = Color.FromArgb(120, 40, 40);
        Color hovered = theme.GetInteractive(own, hovered: true, pressed: false);
        Color held = theme.GetInteractive(own, hovered: true, pressed: true);
        Assert.True(hovered.G > own.G && hovered.B > own.B);
        Assert.True(held.R < own.R);
    }

    [Theory]
    [InlineData("#3478F6", true)]
    [InlineData("#803478F6", true)]
    [InlineData("3478F6", false)]
    [InlineData("#3478F", false)]
    [InlineData("#GGGGGG", false)]
    public void ColoursAreHex(string value, bool valid)
    {
        Assert.Equal(valid, Theme.TryParseColor(value, out Color color));
        if (valid)
        {
            Assert.Equal(value, Theme.FormatColor(color));
        }
    }

    [Theory]
    [InlineData("dark", true)]
    [InlineData("my-theme_2", true)]
    [InlineData("Dark", false)]
    [InlineData("../dark", false)]
    [InlineData("", false)]
    public void NamesAreSafeFileNames(string name, bool valid)
    {
        Assert.Equal(valid, Theme.ValidateName(name) is null);
    }

    [Fact]
    public void AnElementsColourIsARoleOrFixed()
    {
        ThemeColor role = ThemeRole.Selection;
        ThemeColor fixedColor = Color.Red;

        Assert.Equal(Theme.Light[ThemeRole.Selection].ToArgb(), role.Resolve(Theme.Light).ToArgb());
        Assert.Equal(Theme.Dark[ThemeRole.Selection].ToArgb(), role.Resolve(Theme.Dark).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), fixedColor.Resolve(Theme.Light).ToArgb());
        Assert.Equal((ThemeColor)ThemeRole.Selection, role);
        Assert.NotEqual((ThemeColor)ThemeRole.Text, role);
        Assert.Equal((ThemeColor)Color.FromArgb(255, 0, 0), fixedColor);
    }
}
