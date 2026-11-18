namespace RemSox.Viewer;

/// <summary>
/// Mirror of Cosmos.Kernel.System.Keyboard.ConsoleKeyEx. The server casts the key number straight to
/// that enum, so the order here must match it exactly.
/// </summary>
internal enum RemoteKey
{
    NoName, Escape, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, PrintScreen, ScrollLock, PauseBreak,
    Backquote, D1, D2, D3, D4, D5, D6, D7, D8, D9, D0, Minus, Equal, Backslash, Backspace, Tab,
    Q, W, E, R, T, Y, U, I, O, P, LBracket, RBracket, Enter, CapsLock,
    A, S, D, F, G, H, J, K, L, Semicolon, Colon, Apostrophe, LowerThan, BiggerThan, ExclamationPoint,
    LShift, RShift, OEM102, OEM5, Z, X, C, V, B, N, M, Comma, Period, Slash,
    LCtrl, RCtrl, LWin, LAlt, RAlt, Spacebar, AltGr, RWin, Menu,
    Insert, Home, PageUp, Delete, End, PageDown, UpArrow, DownArrow, LeftArrow, RightArrow,
    NumLock, NumDivide, NumMultiply, NumMinus, Num7, Num8, Num9, NumPlus, Num4, Num5, Num6, Num1, Num2, Num3, Num0,
    NumPeriod, NumEnter, Power, Sleep, Wake
}

internal static class RemoteKeyMap
{
    private static readonly Dictionary<Keys, RemoteKey> map = Build();

    /// <summary>
    /// Keys whose meaning comes from the key itself rather than a typed character. They are sent on
    /// key down; every other key is sent from KeyPress so the character matches the local layout.
    /// </summary>
    private static readonly Dictionary<Keys, char> commandKeys = new()
    {
        [Keys.Enter] = '\n',
        [Keys.Back] = '\b',
        [Keys.Tab] = '\t',
        [Keys.Escape] = '\u001b',
        [Keys.Delete] = '\0',
        [Keys.Insert] = '\0',
        [Keys.Home] = '\0',
        [Keys.End] = '\0',
        [Keys.PageUp] = '\0',
        [Keys.PageDown] = '\0',
        [Keys.Up] = '\0',
        [Keys.Down] = '\0',
        [Keys.Left] = '\0',
        [Keys.Right] = '\0',
        [Keys.F1] = '\0', [Keys.F2] = '\0', [Keys.F3] = '\0', [Keys.F4] = '\0', [Keys.F5] = '\0', [Keys.F6] = '\0',
        [Keys.F7] = '\0', [Keys.F8] = '\0', [Keys.F9] = '\0', [Keys.F10] = '\0', [Keys.F11] = '\0', [Keys.F12] = '\0',
    };

    public static RemoteKey Map(Keys key)
    {
        return map.TryGetValue(key, out RemoteKey remote) ? remote : RemoteKey.NoName;
    }

    public static bool TryGetCommandChar(Keys key, out char keyChar)
    {
        return commandKeys.TryGetValue(key, out keyChar);
    }

    private static Dictionary<Keys, RemoteKey> Build()
    {
        Dictionary<Keys, RemoteKey> result = new()
        {
            [Keys.Escape] = RemoteKey.Escape,
            [Keys.Oemtilde] = RemoteKey.Backquote,
            [Keys.OemMinus] = RemoteKey.Minus,
            [Keys.Oemplus] = RemoteKey.Equal,
            [Keys.OemPipe] = RemoteKey.Backslash,
            [Keys.Back] = RemoteKey.Backspace,
            [Keys.Tab] = RemoteKey.Tab,
            [Keys.OemOpenBrackets] = RemoteKey.LBracket,
            [Keys.OemCloseBrackets] = RemoteKey.RBracket,
            [Keys.Enter] = RemoteKey.Enter,
            [Keys.CapsLock] = RemoteKey.CapsLock,
            [Keys.OemSemicolon] = RemoteKey.Semicolon,
            [Keys.OemQuotes] = RemoteKey.Apostrophe,
            [Keys.OemBackslash] = RemoteKey.OEM102,
            [Keys.Oemcomma] = RemoteKey.Comma,
            [Keys.OemPeriod] = RemoteKey.Period,
            [Keys.OemQuestion] = RemoteKey.Slash,
            [Keys.Space] = RemoteKey.Spacebar,
            [Keys.Insert] = RemoteKey.Insert,
            [Keys.Home] = RemoteKey.Home,
            [Keys.PageUp] = RemoteKey.PageUp,
            [Keys.Delete] = RemoteKey.Delete,
            [Keys.End] = RemoteKey.End,
            [Keys.PageDown] = RemoteKey.PageDown,
            [Keys.Up] = RemoteKey.UpArrow,
            [Keys.Down] = RemoteKey.DownArrow,
            [Keys.Left] = RemoteKey.LeftArrow,
            [Keys.Right] = RemoteKey.RightArrow,
            [Keys.NumLock] = RemoteKey.NumLock,
            [Keys.Divide] = RemoteKey.NumDivide,
            [Keys.Multiply] = RemoteKey.NumMultiply,
            [Keys.Subtract] = RemoteKey.NumMinus,
            [Keys.Add] = RemoteKey.NumPlus,
            [Keys.Decimal] = RemoteKey.NumPeriod,
        };

        for (int i = 0; i < 26; i++)
        {
            result[Keys.A + i] = Enum.Parse<RemoteKey>(((char)('A' + i)).ToString());
        }

        for (int i = 0; i <= 9; i++)
        {
            result[Keys.D0 + i] = Enum.Parse<RemoteKey>("D" + i);
            result[Keys.NumPad0 + i] = Enum.Parse<RemoteKey>("Num" + i);
        }

        for (int i = 0; i < 12; i++)
        {
            result[Keys.F1 + i] = RemoteKey.F1 + i;
        }

        return result;
    }
}
