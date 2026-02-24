using Wmux.Core;

namespace Wmux.Terminal;

/// <summary>
/// Virtual screen buffer for a single pane. Tracks character cells, attributes,
/// and cursor position by interpreting VT sequences from the child process.
/// </summary>
public class ScreenBuffer
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public char[][] Chars;
    public ConsoleColor[][] FgColors;
    public ConsoleColor[][] BgColors;
    public bool[][] Bold;
    public int CursorRow;
    public int CursorCol;
    public bool CursorVisible = true;
    public string Title = "";

    /// <summary>
    /// When set, lines scrolled off the top of the screen are saved here.
    /// </summary>
    public ScrollbackBuffer? Scrollback { get; set; }

    // Saved cursor state (for ESC 7 / ESC 8)
    private int _savedCursorRow;
    private int _savedCursorCol;

    // Scroll region
    private int _scrollTop;
    private int _scrollBottom;

    // Current character attributes
    private ConsoleColor _currentFg = ConsoleColor.Gray;
    private ConsoleColor _currentBg = ConsoleColor.Black;
    private bool _currentBold = false;
    private bool _currentDim = false;

    public ScreenBuffer(int width, int height)
    {
        Width = width;
        Height = height;
        _scrollTop = 0;
        _scrollBottom = height - 1;
        Chars = new char[height][];
        FgColors = new ConsoleColor[height][];
        BgColors = new ConsoleColor[height][];
        Bold = new bool[height][];
        for (int r = 0; r < height; r++)
        {
            Chars[r] = new char[width];
            FgColors[r] = new ConsoleColor[width];
            BgColors[r] = new ConsoleColor[width];
            Bold[r] = new bool[width];
            ClearRow(r);
        }
    }

    public void Resize(int newWidth, int newHeight)
    {
        var oldChars = Chars;
        var oldFg = FgColors;
        var oldBg = BgColors;
        var oldBold = Bold;
        int oldHeight = Height;
        int oldWidth = Width;

        Width = newWidth;
        Height = newHeight;
        _scrollTop = 0;
        _scrollBottom = newHeight - 1;

        Chars = new char[newHeight][];
        FgColors = new ConsoleColor[newHeight][];
        BgColors = new ConsoleColor[newHeight][];
        Bold = new bool[newHeight][];

        for (int r = 0; r < newHeight; r++)
        {
            Chars[r] = new char[newWidth];
            FgColors[r] = new ConsoleColor[newWidth];
            BgColors[r] = new ConsoleColor[newWidth];
            Bold[r] = new bool[newWidth];
            ClearRow(r);

            if (r < oldHeight)
            {
                int copyW = Math.Min(oldWidth, newWidth);
                Array.Copy(oldChars[r], Chars[r], copyW);
                Array.Copy(oldFg[r], FgColors[r], copyW);
                Array.Copy(oldBg[r], BgColors[r], copyW);
                Array.Copy(oldBold[r], Bold[r], copyW);
            }
        }

        CursorRow = Math.Min(CursorRow, newHeight - 1);
        CursorCol = Math.Min(CursorCol, newWidth - 1);
    }

    private void ClearRow(int row)
    {
        Array.Fill(Chars[row], ' ');
        Array.Fill(FgColors[row], ConsoleColor.Gray);
        Array.Fill(BgColors[row], ConsoleColor.Black);
        Array.Fill(Bold[row], false);
    }

    private void ClearRow(int row, int startCol, int endCol)
    {
        for (int c = startCol; c <= endCol && c < Width; c++)
        {
            Chars[row][c] = ' ';
            FgColors[row][c] = _currentFg;
            BgColors[row][c] = _currentBg;
            Bold[row][c] = false;
        }
    }

    public void PutChar(char ch)
    {
        if (CursorCol >= Width)
        {
            CursorCol = 0;
            LineFeed();
        }
        if (CursorRow >= 0 && CursorRow < Height && CursorCol >= 0 && CursorCol < Width)
        {
            Chars[CursorRow][CursorCol] = ch;
            FgColors[CursorRow][CursorCol] = _currentDim ? DimColor(_currentFg) : _currentFg;
            BgColors[CursorRow][CursorCol] = _currentBg;
            Bold[CursorRow][CursorCol] = _currentBold;
        }
        CursorCol++;
    }

    public void CarriageReturn() => CursorCol = 0;

    public void LineFeed()
    {
        if (CursorRow == _scrollBottom)
            ScrollUp(1);
        else if (CursorRow < Height - 1)
            CursorRow++;
    }

    public void ReverseIndex()
    {
        if (CursorRow == _scrollTop)
            ScrollDown(1);
        else if (CursorRow > 0)
            CursorRow--;
    }

    public void Backspace()
    {
        if (CursorCol > 0) CursorCol--;
    }

    public void Tab()
    {
        int nextTab = ((CursorCol / 8) + 1) * 8;
        CursorCol = Math.Min(nextTab, Width - 1);
    }

    public void MoveCursor(int row, int col)
    {
        CursorRow = Math.Clamp(row, 0, Height - 1);
        CursorCol = Math.Clamp(col, 0, Width - 1);
    }

    public void MoveCursorUp(int n) => CursorRow = Math.Max(CursorRow - n, _scrollTop);
    public void MoveCursorDown(int n) => CursorRow = Math.Min(CursorRow + n, _scrollBottom);
    public void MoveCursorForward(int n) => CursorCol = Math.Min(CursorCol + n, Width - 1);
    public void MoveCursorBackward(int n) => CursorCol = Math.Max(CursorCol - n, 0);

    public void EraseInDisplay(int mode)
    {
        switch (mode)
        {
            case 0: // cursor to end
                ClearRow(CursorRow, CursorCol, Width - 1);
                for (int r = CursorRow + 1; r < Height; r++) ClearRow(r);
                break;
            case 1: // beginning to cursor
                for (int r = 0; r < CursorRow; r++) ClearRow(r);
                ClearRow(CursorRow, 0, CursorCol);
                break;
            case 2: // entire screen
            case 3:
                for (int r = 0; r < Height; r++) ClearRow(r);
                break;
        }
    }

    public void EraseInLine(int mode)
    {
        switch (mode)
        {
            case 0: ClearRow(CursorRow, CursorCol, Width - 1); break;
            case 1: ClearRow(CursorRow, 0, CursorCol); break;
            case 2: ClearRow(CursorRow); break;
        }
    }

    public void InsertLines(int count)
    {
        for (int i = 0; i < count; i++)
        {
            // Shift lines down within scroll region
            for (int r = _scrollBottom; r > CursorRow; r--)
            {
                Chars[r] = Chars[r - 1];
                FgColors[r] = FgColors[r - 1];
                BgColors[r] = BgColors[r - 1];
                Bold[r] = Bold[r - 1];
            }
            Chars[CursorRow] = new char[Width];
            FgColors[CursorRow] = new ConsoleColor[Width];
            BgColors[CursorRow] = new ConsoleColor[Width];
            Bold[CursorRow] = new bool[Width];
            ClearRow(CursorRow);
        }
    }

    public void DeleteLines(int count)
    {
        for (int i = 0; i < count; i++)
        {
            for (int r = CursorRow; r < _scrollBottom; r++)
            {
                Chars[r] = Chars[r + 1];
                FgColors[r] = FgColors[r + 1];
                BgColors[r] = BgColors[r + 1];
                Bold[r] = Bold[r + 1];
            }
            Chars[_scrollBottom] = new char[Width];
            FgColors[_scrollBottom] = new ConsoleColor[Width];
            BgColors[_scrollBottom] = new ConsoleColor[Width];
            Bold[_scrollBottom] = new bool[Width];
            ClearRow(_scrollBottom);
        }
    }

    public void DeleteChars(int count)
    {
        for (int i = 0; i < count && CursorCol + i < Width; i++)
        {
            for (int c = CursorCol; c < Width - 1; c++)
            {
                Chars[CursorRow][c] = Chars[CursorRow][c + 1];
                FgColors[CursorRow][c] = FgColors[CursorRow][c + 1];
                BgColors[CursorRow][c] = BgColors[CursorRow][c + 1];
                Bold[CursorRow][c] = Bold[CursorRow][c + 1];
            }
            Chars[CursorRow][Width - 1] = ' ';
            FgColors[CursorRow][Width - 1] = _currentFg;
            BgColors[CursorRow][Width - 1] = _currentBg;
            Bold[CursorRow][Width - 1] = false;
        }
    }

    public void InsertChars(int count)
    {
        for (int i = 0; i < count; i++)
        {
            for (int c = Width - 1; c > CursorCol; c--)
            {
                Chars[CursorRow][c] = Chars[CursorRow][c - 1];
                FgColors[CursorRow][c] = FgColors[CursorRow][c - 1];
                BgColors[CursorRow][c] = BgColors[CursorRow][c - 1];
                Bold[CursorRow][c] = Bold[CursorRow][c - 1];
            }
            Chars[CursorRow][CursorCol] = ' ';
            FgColors[CursorRow][CursorCol] = _currentFg;
            BgColors[CursorRow][CursorCol] = _currentBg;
            Bold[CursorRow][CursorCol] = false;
        }
    }

    public void EraseChars(int count)
    {
        for (int c = CursorCol; c < Math.Min(CursorCol + count, Width); c++)
        {
            Chars[CursorRow][c] = ' ';
            FgColors[CursorRow][c] = _currentFg;
            BgColors[CursorRow][c] = _currentBg;
            Bold[CursorRow][c] = false;
        }
    }

    public void SetScrollRegion(int top, int bottom)
    {
        _scrollTop = Math.Clamp(top, 0, Height - 1);
        _scrollBottom = Math.Clamp(bottom, 0, Height - 1);
        if (_scrollTop > _scrollBottom)
        {
            _scrollTop = 0;
            _scrollBottom = Height - 1;
        }
        CursorRow = 0;
        CursorCol = 0;
    }

    public void ScrollUp(int count)
    {
        for (int i = 0; i < count; i++)
        {
            // Save the line being scrolled off to scrollback (only when
            // scrolling the entire screen, not a sub-region).
            if (Scrollback != null && _scrollTop == 0)
            {
                Scrollback.Add(new ScrollbackLine(
                    (char[])Chars[0].Clone(),
                    (ConsoleColor[])FgColors[0].Clone(),
                    (ConsoleColor[])BgColors[0].Clone()));
            }

            for (int r = _scrollTop; r < _scrollBottom; r++)
            {
                Chars[r] = Chars[r + 1];
                FgColors[r] = FgColors[r + 1];
                BgColors[r] = BgColors[r + 1];
                Bold[r] = Bold[r + 1];
            }
            Chars[_scrollBottom] = new char[Width];
            FgColors[_scrollBottom] = new ConsoleColor[Width];
            BgColors[_scrollBottom] = new ConsoleColor[Width];
            Bold[_scrollBottom] = new bool[Width];
            ClearRow(_scrollBottom);
        }
    }

    public void ScrollDown(int count)
    {
        for (int i = 0; i < count; i++)
        {
            for (int r = _scrollBottom; r > _scrollTop; r--)
            {
                Chars[r] = Chars[r - 1];
                FgColors[r] = FgColors[r - 1];
                BgColors[r] = BgColors[r - 1];
                Bold[r] = Bold[r - 1];
            }
            Chars[_scrollTop] = new char[Width];
            FgColors[_scrollTop] = new ConsoleColor[Width];
            BgColors[_scrollTop] = new ConsoleColor[Width];
            Bold[_scrollTop] = new bool[Width];
            ClearRow(_scrollTop);
        }
    }

    public void SaveCursor()
    {
        _savedCursorRow = CursorRow;
        _savedCursorCol = CursorCol;
    }

    public void RestoreCursor()
    {
        CursorRow = _savedCursorRow;
        CursorCol = _savedCursorCol;
    }

    public void SetGraphicsRendition(int[] parameters)
    {
        if (parameters.Length == 0)
        {
            ResetAttributes();
            return;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            int p = parameters[i];
            switch (p)
            {
                case 0: ResetAttributes(); break;
                case 1: _currentBold = true; break;
                case 2: _currentDim = true; break;
                case 22: _currentBold = false; _currentDim = false; break;
                case 7: // Reverse video
                    (_currentFg, _currentBg) = (_currentBg, _currentFg);
                    break;
                case 27: // Reverse off
                    _currentFg = ConsoleColor.Gray;
                    _currentBg = ConsoleColor.Black;
                    break;
                case 30: _currentFg = ConsoleColor.Black; break;
                case 31: _currentFg = ConsoleColor.DarkRed; break;
                case 32: _currentFg = ConsoleColor.DarkGreen; break;
                case 33: _currentFg = ConsoleColor.DarkYellow; break;
                case 34: _currentFg = ConsoleColor.DarkBlue; break;
                case 35: _currentFg = ConsoleColor.DarkMagenta; break;
                case 36: _currentFg = ConsoleColor.DarkCyan; break;
                case 37: _currentFg = ConsoleColor.Gray; break;
                case 39: _currentFg = ConsoleColor.Gray; break; // default fg
                case 40: _currentBg = ConsoleColor.Black; break;
                case 41: _currentBg = ConsoleColor.DarkRed; break;
                case 42: _currentBg = ConsoleColor.DarkGreen; break;
                case 43: _currentBg = ConsoleColor.DarkYellow; break;
                case 44: _currentBg = ConsoleColor.DarkBlue; break;
                case 45: _currentBg = ConsoleColor.DarkMagenta; break;
                case 46: _currentBg = ConsoleColor.DarkCyan; break;
                case 47: _currentBg = ConsoleColor.Gray; break;
                case 49: _currentBg = ConsoleColor.Black; break; // default bg
                case 90: _currentFg = ConsoleColor.DarkGray; break;
                case 91: _currentFg = ConsoleColor.Red; break;
                case 92: _currentFg = ConsoleColor.Green; break;
                case 93: _currentFg = ConsoleColor.Yellow; break;
                case 94: _currentFg = ConsoleColor.Blue; break;
                case 95: _currentFg = ConsoleColor.Magenta; break;
                case 96: _currentFg = ConsoleColor.Cyan; break;
                case 97: _currentFg = ConsoleColor.White; break;
                case 100: _currentBg = ConsoleColor.DarkGray; break;
                case 101: _currentBg = ConsoleColor.Red; break;
                case 102: _currentBg = ConsoleColor.Green; break;
                case 103: _currentBg = ConsoleColor.Yellow; break;
                case 104: _currentBg = ConsoleColor.Blue; break;
                case 105: _currentBg = ConsoleColor.Magenta; break;
                case 106: _currentBg = ConsoleColor.Cyan; break;
                case 107: _currentBg = ConsoleColor.White; break;
                case 38: // Extended foreground (256-color / truecolor)
                    if (i + 1 < parameters.Length && parameters[i + 1] == 5 && i + 2 < parameters.Length)
                    {
                        _currentFg = Map256ToConsoleColor(parameters[i + 2]);
                        i += 2;
                    }
                    else if (i + 1 < parameters.Length && parameters[i + 1] == 2 && i + 4 < parameters.Length)
                    {
                        _currentFg = MapRgbToConsoleColor(parameters[i + 2], parameters[i + 3], parameters[i + 4]);
                        i += 4;
                    }
                    break;
                case 48: // Extended background
                    if (i + 1 < parameters.Length && parameters[i + 1] == 5 && i + 2 < parameters.Length)
                    {
                        _currentBg = Map256ToConsoleColor(parameters[i + 2]);
                        i += 2;
                    }
                    else if (i + 1 < parameters.Length && parameters[i + 1] == 2 && i + 4 < parameters.Length)
                    {
                        _currentBg = MapRgbToConsoleColor(parameters[i + 2], parameters[i + 3], parameters[i + 4]);
                        i += 4;
                    }
                    break;
            }
        }
    }

    private void ResetAttributes()
    {
        _currentFg = ConsoleColor.Gray;
        _currentBg = ConsoleColor.Black;
        _currentBold = false;
        _currentDim = false;
    }

    private static ConsoleColor DimColor(ConsoleColor c) => c switch
    {
        ConsoleColor.White => ConsoleColor.DarkGray,
        ConsoleColor.Gray => ConsoleColor.DarkGray,
        ConsoleColor.Red => ConsoleColor.DarkRed,
        ConsoleColor.Green => ConsoleColor.DarkGreen,
        ConsoleColor.Yellow => ConsoleColor.DarkYellow,
        ConsoleColor.Blue => ConsoleColor.DarkBlue,
        ConsoleColor.Magenta => ConsoleColor.DarkMagenta,
        ConsoleColor.Cyan => ConsoleColor.DarkCyan,
        _ => c // Already dark or black
    };

    private static ConsoleColor Map256ToConsoleColor(int color)
    {
        return color switch
        {
            0 => ConsoleColor.Black,
            1 => ConsoleColor.DarkRed,
            2 => ConsoleColor.DarkGreen,
            3 => ConsoleColor.DarkYellow,
            4 => ConsoleColor.DarkBlue,
            5 => ConsoleColor.DarkMagenta,
            6 => ConsoleColor.DarkCyan,
            7 => ConsoleColor.Gray,
            8 => ConsoleColor.DarkGray,
            9 => ConsoleColor.Red,
            10 => ConsoleColor.Green,
            11 => ConsoleColor.Yellow,
            12 => ConsoleColor.Blue,
            13 => ConsoleColor.Magenta,
            14 => ConsoleColor.Cyan,
            15 => ConsoleColor.White,
            _ => ConsoleColor.Gray
        };
    }

    private static ConsoleColor MapRgbToConsoleColor(int r, int g, int b)
    {
        int brightness = (r + g + b) / 3;
        if (brightness < 64) return ConsoleColor.Black;
        if (brightness > 192) return ConsoleColor.White;
        if (r > g && r > b) return ConsoleColor.Red;
        if (g > r && g > b) return ConsoleColor.Green;
        if (b > r && b > g) return ConsoleColor.Blue;
        if (r > 128 && g > 128) return ConsoleColor.Yellow;
        if (r > 128 && b > 128) return ConsoleColor.Magenta;
        if (g > 128 && b > 128) return ConsoleColor.Cyan;
        return ConsoleColor.Gray;
    }
}
