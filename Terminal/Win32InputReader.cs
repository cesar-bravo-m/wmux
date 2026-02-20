using static Wmux.Terminal.ConPtyNative;

namespace Wmux.Terminal;

/// <summary>
/// Low-level console input reader that uses the Win32 ReadConsoleInput API
/// to read KEY_EVENT_RECORD structs directly from the console input buffer.
/// This completely bypasses Console.ReadKey, which can echo control characters
/// (like ^B) even when called with intercept=true.
///
/// Modelled after the Unix readline concept: intercepts raw input at the
/// OS level and converts it into ConsoleKeyInfo objects for the rest of the
/// application to consume.
/// </summary>
public sealed class Win32InputReader : IDisposable
{
    private readonly IntPtr _inputHandle;
    private readonly INPUT_RECORD[] _buffer = new INPUT_RECORD[16];
    private volatile bool _disposed;

    /// <summary>
    /// Fired when the console window is resized. The handler receives
    /// the new width and height.
    /// </summary>
    public event Action<int, int>? WindowResized;

    public Win32InputReader()
    {
        _inputHandle = GetStdHandle(STD_INPUT_HANDLE);
    }

    /// <summary>
    /// Blocking read that returns the next key-down event as a ConsoleKeyInfo.
    /// Also dispatches WindowResized events if a resize record is encountered.
    /// This method blocks until a key-down event is available.
    /// </summary>
    public ConsoleKeyInfo? ReadKey()
    {
        while (!_disposed)
        {
            if (!ReadConsoleInput(_inputHandle, _buffer, 1, out uint eventsRead))
                return null;

            if (eventsRead == 0)
                continue;

            ref var rec = ref _buffer[0];

            switch (rec.EventType)
            {
                case KEY_EVENT:
                    // Only process key-down events
                    if (rec.KeyEvent.bKeyDown == 0)
                        continue;

                    var keyInfo = ConvertToConsoleKeyInfo(ref rec.KeyEvent);
                    if (keyInfo != null)
                        return keyInfo;
                    continue;

                case WINDOW_BUFFER_SIZE_EVENT:
                    int w = rec.WindowBufferSizeEvent.dwSize.X;
                    int h = rec.WindowBufferSizeEvent.dwSize.Y;
                    WindowResized?.Invoke(w, h);
                    continue;

                default:
                    // Ignore mouse events, focus events, menu events
                    continue;
            }
        }

        return null;
    }

    /// <summary>
    /// Non-blocking check if there are any input events waiting.
    /// </summary>
    public bool KeyAvailable()
    {
        if (!GetNumberOfConsoleInputEvents(_inputHandle, out uint count))
            return false;
        return count > 0;
    }

    /// <summary>
    /// Converts a Win32 KEY_EVENT_RECORD into a .NET ConsoleKeyInfo.
    /// Returns null for events that shouldn't be forwarded (e.g. bare
    /// modifier key presses like Shift alone).
    /// </summary>
    private static ConsoleKeyInfo? ConvertToConsoleKeyInfo(ref KEY_EVENT_RECORD keyEvent)
    {
        char ch = keyEvent.UnicodeChar;
        ushort vk = keyEvent.wVirtualKeyCode;
        uint ctrlState = keyEvent.dwControlKeyState;

        // Skip bare modifier key presses (Shift, Ctrl, Alt alone)
        if (vk is 0x10 or 0x11 or 0x12 // VK_SHIFT, VK_CONTROL, VK_MENU
            or 0xA0 or 0xA1             // VK_LSHIFT, VK_RSHIFT
            or 0xA2 or 0xA3             // VK_LCONTROL, VK_RCONTROL
            or 0xA4 or 0xA5             // VK_LMENU, VK_RMENU
            or 0x5B or 0x5C             // VK_LWIN, VK_RWIN
            or 0x14                     // VK_CAPITAL (Caps Lock)
            or 0x90                     // VK_NUMLOCK
            or 0x91)                    // VK_SCROLL
        {
            return null;
        }

        bool shift = (ctrlState & SHIFT_PRESSED) != 0;
        bool alt = (ctrlState & (LEFT_ALT_PRESSED | RIGHT_ALT_PRESSED)) != 0;
        bool ctrl = (ctrlState & (LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED)) != 0;

        ConsoleKey consoleKey = MapVirtualKeyToConsoleKey(vk);

        // For Ctrl+letter, the UnicodeChar comes through as 0x01-0x1A.
        // We still need to report the correct KeyChar for the rest of
        // the application (InputHandler checks key.KeyChar for bindings).
        // ConsoleKeyInfo stores the actual char the user typed, so we
        // keep whatever the OS gave us.

        return new ConsoleKeyInfo(ch, consoleKey, shift, alt, ctrl);
    }

    /// <summary>
    /// Maps a Win32 virtual-key code to a .NET ConsoleKey enum value.
    /// </summary>
    private static ConsoleKey MapVirtualKeyToConsoleKey(ushort vk)
    {
        return vk switch
        {
            VK_BACK   => ConsoleKey.Backspace,
            VK_TAB    => ConsoleKey.Tab,
            VK_RETURN => ConsoleKey.Enter,
            VK_ESCAPE => ConsoleKey.Escape,
            VK_PRIOR  => ConsoleKey.PageUp,
            VK_NEXT   => ConsoleKey.PageDown,
            VK_END    => ConsoleKey.End,
            VK_HOME   => ConsoleKey.Home,
            VK_LEFT   => ConsoleKey.LeftArrow,
            VK_UP     => ConsoleKey.UpArrow,
            VK_RIGHT  => ConsoleKey.RightArrow,
            VK_DOWN   => ConsoleKey.DownArrow,
            VK_INSERT => ConsoleKey.Insert,
            VK_DELETE => ConsoleKey.Delete,
            VK_F1     => ConsoleKey.F1,
            VK_F2     => ConsoleKey.F2,
            VK_F3     => ConsoleKey.F3,
            VK_F4     => ConsoleKey.F4,
            VK_F5     => ConsoleKey.F5,
            VK_F6     => ConsoleKey.F6,
            VK_F7     => ConsoleKey.F7,
            VK_F8     => ConsoleKey.F8,
            VK_F9     => ConsoleKey.F9,
            VK_F10    => ConsoleKey.F10,
            VK_F11    => ConsoleKey.F11,
            VK_F12    => ConsoleKey.F12,
            0x20      => ConsoleKey.Spacebar,   // VK_SPACE

            // 0-9 (VK_0 .. VK_9 = 0x30 .. 0x39)
            >= 0x30 and <= 0x39 => (ConsoleKey)(ConsoleKey.D0 + (vk - 0x30)),

            // A-Z (VK_A .. VK_Z = 0x41 .. 0x5A)
            >= 0x41 and <= 0x5A => (ConsoleKey)(ConsoleKey.A + (vk - 0x41)),

            // Numpad 0-9
            >= 0x60 and <= 0x69 => (ConsoleKey)(ConsoleKey.NumPad0 + (vk - 0x60)),

            0x6A => ConsoleKey.Multiply,
            0x6B => ConsoleKey.Add,
            0x6C => ConsoleKey.Separator,
            0x6D => ConsoleKey.Subtract,
            0x6E => ConsoleKey.Decimal,
            0x6F => ConsoleKey.Divide,

            // OEM keys
            0xBA => ConsoleKey.Oem1,        // ;:
            0xBB => ConsoleKey.OemPlus,     // =+
            0xBC => ConsoleKey.OemComma,    // ,<
            0xBD => ConsoleKey.OemMinus,    // -_
            0xBE => ConsoleKey.OemPeriod,   // .>
            0xBF => ConsoleKey.Oem2,        // /?
            0xC0 => ConsoleKey.Oem3,        // `~
            0xDB => ConsoleKey.Oem4,        // [{
            0xDC => ConsoleKey.Oem5,        // \|
            0xDD => ConsoleKey.Oem6,        // ]}
            0xDE => ConsoleKey.Oem7,        // '"
            0xDF => ConsoleKey.Oem8,

            _ => (ConsoleKey)vk // Best-effort fallback
        };
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
