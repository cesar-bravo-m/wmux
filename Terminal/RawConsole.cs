using static Wmux.Terminal.ConPtyNative;

namespace Wmux.Terminal;

/// <summary>
/// Puts the Windows console into raw mode by calling SetConsoleMode.
/// Disables echo, line input, processed input, and quick-edit so that
/// all key presses (including Ctrl+B) are delivered without any
/// system-level processing or on-screen echoing.
/// </summary>
public static class RawConsole
{
    private static uint _savedInputMode;
    private static uint _savedOutputMode;
    private static IntPtr _inputHandle;
    private static IntPtr _outputHandle;
    private static bool _enabled;

    /// <summary>
    /// Switch the console to raw mode. Must be called before the input loop.
    /// Returns false if the Win32 calls fail (e.g. not attached to a console).
    /// </summary>
    public static bool Enable()
    {
        _inputHandle = GetStdHandle(STD_INPUT_HANDLE);
        _outputHandle = GetStdHandle(STD_OUTPUT_HANDLE);

        if (!GetConsoleMode(_inputHandle, out _savedInputMode))
            return false;
        if (!GetConsoleMode(_outputHandle, out _savedOutputMode))
            return false;

        // Raw input: nothing echoed, nothing buffered, nothing processed.
        // Keep WINDOW_INPUT so we still get resize events.
        // Do NOT set ENABLE_VIRTUAL_TERMINAL_INPUT — that flag causes Windows
        // to split special keys (arrows, F-keys) into individual VT character
        // events via ReadConsoleInput, which conflicts with Win32InputReader +
        // KeyToVtSequence already doing the conversion. The split events arrive
        // as separate writes to ConPTY with timing gaps, causing PSReadLine to
        // treat the ESC as standalone and echo the rest as literal text.
        uint rawInput = ENABLE_WINDOW_INPUT
                      | ENABLE_EXTENDED_FLAGS; // required to clear QUICK_EDIT

        if (!SetConsoleMode(_inputHandle, rawInput))
            return false;

        // Output: enable VT processing so our ANSI sequences are rendered,
        // and DISABLE_NEWLINE_AUTO_RETURN for correct bottom-right-corner writes.
        uint rawOutput = ENABLE_PROCESSED_OUTPUT
                       | ENABLE_WRAP_AT_EOL_OUTPUT
                       | ENABLE_VIRTUAL_TERMINAL_PROCESSING
                       | DISABLE_NEWLINE_AUTO_RETURN;

        if (!SetConsoleMode(_outputHandle, rawOutput))
        {
            // Fall back to the basics if VT processing is not supported.
            rawOutput = ENABLE_PROCESSED_OUTPUT | ENABLE_WRAP_AT_EOL_OUTPUT;
            SetConsoleMode(_outputHandle, rawOutput);
        }

        _enabled = true;
        return true;
    }

    /// <summary>
    /// Restore the original console mode. Call on exit.
    /// </summary>
    public static void Restore()
    {
        if (!_enabled) return;
        SetConsoleMode(_inputHandle, _savedInputMode);
        SetConsoleMode(_outputHandle, _savedOutputMode);
        _enabled = false;
    }
}
