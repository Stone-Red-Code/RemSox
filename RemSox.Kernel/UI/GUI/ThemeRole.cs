namespace RemSox.Kernel.UI.GUI;

/// <summary>
/// What a colour of the theme is for (<see cref="Theme"/>). Controls, the window frame, the
/// desktop and programs name one of these instead of a colour, so the account's theme decides
/// how they look. The values index <see cref="Theme"/>'s colours, in the order of its keys.
/// </summary>
public enum ThemeRole
{
    /// <summary>A window's client area.</summary>
    Background,

    /// <summary>Raised parts: the title bar, the taskbar, the start menu, panels.</summary>
    Surface,

    /// <summary>Inside text boxes, text areas and lists.</summary>
    Field,

    /// <summary>A button at rest.</summary>
    Control,

    /// <summary>A button under the pointer.</summary>
    ControlHover,

    /// <summary>A button held down.</summary>
    ControlPressed,

    /// <summary>Hairlines: control and window frames, separators.</summary>
    Border,

    /// <summary>The frame of the focused window and field.</summary>
    BorderFocus,

    /// <summary>Text.</summary>
    Text,

    /// <summary>Secondary text: status lines, an inactive title.</summary>
    TextMuted,

    /// <summary>Greyed-out text: something that cannot be used.</summary>
    TextDisabled,

    /// <summary>The one emphasised thing: the start button, a default button, the active task.</summary>
    Accent,

    /// <summary>An accent button under the pointer.</summary>
    AccentHover,

    /// <summary>Text on <see cref="Accent"/>.</summary>
    TextOnAccent,

    /// <summary>A selected row, selected text.</summary>
    Selection,

    /// <summary>Destructive or failed: the close button, Restart and Shut down, error messages.</summary>
    Danger,

    /// <summary>The terminal's screen.</summary>
    TerminalBackground,

    /// <summary>The terminal's text.</summary>
    TerminalText,

    /// <summary>The terminal's error lines.</summary>
    TerminalError,
}
