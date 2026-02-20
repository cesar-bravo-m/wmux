namespace Wmux.Terminal;

/// <summary>
/// Parses VT100/xterm escape sequences from a child process output stream
/// and applies them to a ScreenBuffer.
/// </summary>
public class VtParser
{
    private enum State { Ground, Escape, Csi, CsiParam, OscString, DcsString }

    private State _state = State.Ground;
    private readonly List<int> _params = new();
    private int _currentParam = -1;
    private char _intermediateChar = '\0';
    private readonly List<char> _oscString = new();

    public void Process(ScreenBuffer screen, ReadOnlySpan<char> data)
    {
        foreach (char ch in data)
        {
            switch (_state)
            {
                case State.Ground:
                    ProcessGround(screen, ch);
                    break;
                case State.Escape:
                    ProcessEscape(screen, ch);
                    break;
                case State.Csi:
                case State.CsiParam:
                    ProcessCsi(screen, ch);
                    break;
                case State.OscString:
                    ProcessOsc(screen, ch);
                    break;
                case State.DcsString:
                    if (ch == '\x1b' || ch == '\x9c')
                        _state = State.Ground;
                    break;
            }
        }
    }

    private void ProcessGround(ScreenBuffer screen, char ch)
    {
        switch (ch)
        {
            case '\x1b': // ESC
                _state = State.Escape;
                _intermediateChar = '\0';
                break;
            case '\r':
                screen.CarriageReturn();
                break;
            case '\n':
            case '\x0b': // VT
            case '\x0c': // FF
                screen.LineFeed();
                break;
            case '\b':
                screen.Backspace();
                break;
            case '\t':
                screen.Tab();
                break;
            case '\x07': // BEL - ignore
                break;
            case '\0':
                break;
            default:
                if (ch >= ' ')
                    screen.PutChar(ch);
                break;
        }
    }

    private void ProcessEscape(ScreenBuffer screen, char ch)
    {
        switch (ch)
        {
            case '[': // CSI
                _state = State.Csi;
                _params.Clear();
                _currentParam = -1;
                _intermediateChar = '\0';
                break;
            case ']': // OSC
                _state = State.OscString;
                _oscString.Clear();
                break;
            case 'P': // DCS
                _state = State.DcsString;
                break;
            case '7': // Save cursor
                screen.SaveCursor();
                _state = State.Ground;
                break;
            case '8': // Restore cursor
                screen.RestoreCursor();
                _state = State.Ground;
                break;
            case 'M': // Reverse index
                screen.ReverseIndex();
                _state = State.Ground;
                break;
            case 'D': // Index (line feed)
                screen.LineFeed();
                _state = State.Ground;
                break;
            case 'E': // Next line
                screen.CarriageReturn();
                screen.LineFeed();
                _state = State.Ground;
                break;
            case 'c': // Reset
                screen.EraseInDisplay(2);
                screen.MoveCursor(0, 0);
                _state = State.Ground;
                break;
            case '(': case ')': case '*': case '+': // Character set designation
                _intermediateChar = ch;
                _state = State.Ground; // Simplified: skip the next char
                break;
            case '=': case '>': // Application/Normal keypad mode
                _state = State.Ground;
                break;
            default:
                _state = State.Ground;
                break;
        }
    }

    private void ProcessCsi(ScreenBuffer screen, char ch)
    {
        if (ch >= '0' && ch <= '9')
        {
            if (_currentParam < 0) _currentParam = 0;
            _currentParam = _currentParam * 10 + (ch - '0');
            _state = State.CsiParam;
            return;
        }

        if (ch == ';')
        {
            _params.Add(_currentParam < 0 ? 0 : _currentParam);
            _currentParam = -1;
            _state = State.CsiParam;
            return;
        }

        if (ch == '?' || ch == '>' || ch == '!')
        {
            _intermediateChar = ch;
            return;
        }

        if (ch == ' ' || ch == '"' || ch == '\'')
        {
            _intermediateChar = ch;
            return;
        }

        // Final byte - execute
        if (_currentParam >= 0)
            _params.Add(_currentParam);

        ExecuteCsi(screen, ch);
        _state = State.Ground;
    }

    private int Param(int index, int defaultVal = 0)
    {
        if (index < _params.Count && _params[index] > 0)
            return _params[index];
        return defaultVal;
    }

    private void ExecuteCsi(ScreenBuffer screen, char ch)
    {
        if (_intermediateChar == '?')
        {
            ExecuteDecPrivateMode(screen, ch);
            return;
        }

        switch (ch)
        {
            case 'A': // Cursor up
                screen.MoveCursorUp(Param(0, 1));
                break;
            case 'B': // Cursor down
                screen.MoveCursorDown(Param(0, 1));
                break;
            case 'C': // Cursor forward
                screen.MoveCursorForward(Param(0, 1));
                break;
            case 'D': // Cursor backward
                screen.MoveCursorBackward(Param(0, 1));
                break;
            case 'E': // Cursor next line
                screen.CursorCol = 0;
                screen.MoveCursorDown(Param(0, 1));
                break;
            case 'F': // Cursor previous line
                screen.CursorCol = 0;
                screen.MoveCursorUp(Param(0, 1));
                break;
            case 'G': // Cursor horizontal absolute
                screen.CursorCol = Math.Clamp(Param(0, 1) - 1, 0, screen.Width - 1);
                break;
            case 'H': // Cursor position
            case 'f':
                screen.MoveCursor(Param(0, 1) - 1, Param(1, 1) - 1);
                break;
            case 'J': // Erase in display
                screen.EraseInDisplay(Param(0));
                break;
            case 'K': // Erase in line
                screen.EraseInLine(Param(0));
                break;
            case 'L': // Insert lines
                screen.InsertLines(Param(0, 1));
                break;
            case 'M': // Delete lines
                screen.DeleteLines(Param(0, 1));
                break;
            case 'P': // Delete characters
                screen.DeleteChars(Param(0, 1));
                break;
            case '@': // Insert characters
                screen.InsertChars(Param(0, 1));
                break;
            case 'X': // Erase characters
                screen.EraseChars(Param(0, 1));
                break;
            case 'S': // Scroll up
                screen.ScrollUp(Param(0, 1));
                break;
            case 'T': // Scroll down
                screen.ScrollDown(Param(0, 1));
                break;
            case 'd': // Cursor vertical absolute
                screen.CursorRow = Math.Clamp(Param(0, 1) - 1, 0, screen.Height - 1);
                break;
            case 'm': // Set Graphics Rendition
                screen.SetGraphicsRendition(_params.Count == 0 ? [0] : _params.ToArray());
                break;
            case 'r': // Set scroll region
                screen.SetScrollRegion(Param(0, 1) - 1, Param(1, screen.Height) - 1);
                break;
            case 's': // Save cursor
                screen.SaveCursor();
                break;
            case 'u': // Restore cursor
                screen.RestoreCursor();
                break;
            case 'n': // Device status report
                // We don't respond to queries in this simplified parser
                break;
            case 'c': // Send device attributes
                break;
            case 't': // Window manipulation - ignore
                break;
            case 'l': // Reset mode
            case 'h': // Set mode
                break;
        }
    }

    private void ExecuteDecPrivateMode(ScreenBuffer screen, char ch)
    {
        int mode = Param(0);
        switch (ch)
        {
            case 'h': // Set
                switch (mode)
                {
                    case 25: screen.CursorVisible = true; break;
                    case 1049: // Alt screen buffer
                        screen.EraseInDisplay(2);
                        screen.MoveCursor(0, 0);
                        break;
                }
                break;
            case 'l': // Reset
                switch (mode)
                {
                    case 25: screen.CursorVisible = false; break;
                    case 1049: // Main screen buffer
                        screen.EraseInDisplay(2);
                        screen.MoveCursor(0, 0);
                        break;
                }
                break;
        }
    }

    private void ProcessOsc(ScreenBuffer screen, char ch)
    {
        if (ch == '\x07' || ch == '\x1b') // BEL or ESC terminates OSC
        {
            var str = new string(_oscString.ToArray());
            var semi = str.IndexOf(';');
            if (semi >= 0)
            {
                var cmd = str[..semi];
                var value = str[(semi + 1)..];
                if (cmd == "0" || cmd == "2")
                    screen.Title = value;
            }
            _state = ch == '\x1b' ? State.Escape : State.Ground;
            // If ESC, we might see '\' next for ST - handle in Escape
            if (ch == '\x1b')
                _state = State.Ground; // Simplified
        }
        else
        {
            _oscString.Add(ch);
        }
    }
}
