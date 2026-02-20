using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Wmux.Core;

namespace Wmux.Terminal;

/// <summary>
/// A Win32 GUI window that acts as a terminal emulator.
/// Renders a character grid using GDI+ and captures all keyboard
/// input directly via WM_KEYDOWN / WM_CHAR at the WndProc level,
/// completely bypassing the console subsystem. This means Ctrl+B
/// and all other control keys are received cleanly without echoing
/// or interception.
///
/// Keyboard strategy:
///   - ALL input is handled in WndProc via WM_KEYDOWN and WM_CHAR.
///   - WM_KEYDOWN handles: Ctrl+letter, special keys (arrows, F-keys,
///     Home/End/PgUp/PgDn/Ins/Del), and sets a flag to suppress
///     the corresponding WM_CHAR if the key was already dispatched.
///   - WM_CHAR handles: printable characters (where we want the actual
///     Unicode character from the OS keyboard layout, not our own
///     key-code-to-char translation).
///   - WM_SYSKEYDOWN handles Alt+key combinations.
///   - This single-path approach prevents double-firing entirely.
/// </summary>
public sealed class TerminalWindow : Form
{
    // ── Win32 message constants ─────────────────────────────
    private const int WM_KEYDOWN    = 0x0100;
    private const int WM_KEYUP      = 0x0101;
    private const int WM_CHAR       = 0x0102;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP   = 0x0105;
    private const int WM_SYSCHAR    = 0x0106;

    // ── Font and cell metrics ───────────────────────────────
    private Font _font;
    private int _cellWidth;
    private int _cellHeight;
    private int _cols;
    private int _rows;

    // ── Screen buffer (character grid) ──────────────────────
    private char[,] _chars;
    private ConsoleColor[,] _fg;
    private ConsoleColor[,] _bg;
    private int _cursorRow;
    private int _cursorCol;
    private bool _cursorVisible = true;
    private readonly object _bufferLock = new();

    // ── Cached brushes for the 16 ConsoleColor values ───────
    private readonly SolidBrush[] _brushCache = new SolidBrush[16];

    // ── Blinking cursor ─────────────────────────────────────
    private System.Windows.Forms.Timer _cursorTimer;
    private bool _cursorBlink;
    private static readonly SolidBrush _cursorBrush = new(Color.FromArgb(200, 220, 220, 220));

    // ── Coalesced repaint ───────────────────────────────────
    private volatile bool _paintPending;

    // ── Keyboard ────────────────────────────────────────────
    /// <summary>
    /// Fired for every key event. The handler receives a ConsoleKeyInfo
    /// just like the old console-based input path.
    /// </summary>
    public event Action<ConsoleKeyInfo>? KeyPressed;

    /// <summary>
    /// Fired when the terminal grid size changes (cols, rows).
    /// </summary>
    public event Action<int, int>? TerminalResized;

    /// <summary>
    /// Fired when the user closes the window.
    /// </summary>
    public event Action? WindowClosed;

    public int Cols => _cols;
    public int Rows => _rows;

    public TerminalWindow(string title = "wmux", int initialCols = 120, int initialRows = 30)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        DoubleBuffered = true;

        // Pick a good monospace font
        _font = FindMonospaceFont(14f);
        MeasureCellSize();

        _cols = initialCols;
        _rows = initialRows;

        // Initialize character grid
        _chars = new char[_rows, _cols];
        _fg = new ConsoleColor[_rows, _cols];
        _bg = new ConsoleColor[_rows, _cols];
        ClearGrid();

        // Set the window client size to fit the grid
        ClientSize = new Size(_cols * _cellWidth, _rows * _cellHeight);
        MinimumSize = new Size(_cellWidth * 20 + (Width - ClientSize.Width),
                               _cellHeight * 5 + (Height - ClientSize.Height));

        // Cursor blink timer
        _cursorTimer = new System.Windows.Forms.Timer { Interval = 530 };
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorBlink = !_cursorBlink;
            InvalidateCursorCell();
        };
        _cursorTimer.Start();

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        // Pre-cache brushes for all 16 ConsoleColor values
        for (int i = 0; i < 16; i++)
            _brushCache[i] = new SolidBrush(ConsoleColorToColor((ConsoleColor)i));
    }

    // ── Public API ──────────────────────────────────────────

    /// <summary>
    /// Update the entire character grid. Called from a background thread.
    /// Uses a dirty flag to coalesce rapid updates — only one BeginInvoke
    /// is posted at a time so the message pump is never flooded.
    /// </summary>
    public void UpdateGrid(char[,] chars, ConsoleColor[,] fg, ConsoleColor[,] bg,
                           int cursorRow, int cursorCol, bool cursorVisible)
    {
        lock (_bufferLock)
        {
            int rows = chars.GetLength(0);
            int cols = chars.GetLength(1);

            if (rows != _chars.GetLength(0) || cols != _chars.GetLength(1))
            {
                _chars = new char[rows, cols];
                _fg = new ConsoleColor[rows, cols];
                _bg = new ConsoleColor[rows, cols];
            }

            Array.Copy(chars, _chars, chars.Length);
            Array.Copy(fg, _fg, fg.Length);
            Array.Copy(bg, _bg, bg.Length);
            _cursorRow = cursorRow;
            _cursorCol = cursorCol;
            _cursorVisible = cursorVisible;
        }

        // Only post one Invalidate at a time. If a previous one hasn't
        // been processed yet, skip — the pending repaint will pick up
        // the latest buffer contents.
        if (!_paintPending && IsHandleCreated && !IsDisposed)
        {
            _paintPending = true;
            try
            {
                BeginInvoke(() =>
                {
                    _paintPending = false;
                    Invalidate();
                    Update();
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
    }

    /// <summary>
    /// Thread-safe way to set the window title.
    /// </summary>
    public void SetTitle(string title)
    {
        if (InvokeRequired)
        {
            try { BeginInvoke(() => Text = title); }
            catch { }
        }
        else
        {
            Text = title;
        }
    }

    // ── Keyboard handling — unified WndProc approach ────────

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_KEYDOWN:
            {
                bool handled = HandleKeyDown(m.WParam, alt: false);
                if (handled)
                    return; // Swallow — we already dispatched
                // Not handled: pass to base so the framework translates
                // this WM_KEYDOWN into exactly one WM_CHAR for us.
                base.WndProc(ref m);
                return;
            }

            case WM_SYSKEYDOWN:
            {
                // Let Alt+F4 through so Windows can generate WM_SYSCOMMAND SC_CLOSE
                int vk = m.WParam.ToInt32();
                if (vk == 0x73) // VK_F4
                {
                    base.WndProc(ref m);
                    return;
                }

                bool handled = HandleKeyDown(m.WParam, alt: true);
                if (handled)
                    return; // Swallow — prevent menu activation, system beep
                // Alt+printable: unlikely but let framework translate
                base.WndProc(ref m);
                return;
            }

            case WM_CHAR:
            case WM_SYSCHAR:
                HandleChar(m.WParam);
                return; // Always swallow — we dispatch here

            case WM_KEYUP:
            case WM_SYSKEYUP:
                // Swallow to prevent default behaviour (system beeps etc.)
                return;

            default:
                base.WndProc(ref m);
                return;
        }
    }

    /// <summary>
    /// Handle WM_KEYDOWN / WM_SYSKEYDOWN. Returns true if the key was
    /// dispatched here (caller should swallow the message). Returns false
    /// if the key should be left for WM_CHAR to handle (caller should
    /// pass to base.WndProc so the framework translates it once).
    /// </summary>
    private bool HandleKeyDown(IntPtr wParam, bool alt)
    {
        int vk = wParam.ToInt32();
        bool shift = (ModifierKeys & Keys.Shift) != 0;
        bool ctrl  = (ModifierKeys & Keys.Control) != 0;

        // Skip bare modifier key presses
        if (vk is 0x10 or 0x11 or 0x12    // VK_SHIFT, VK_CONTROL, VK_MENU
            or 0xA0 or 0xA1               // VK_LSHIFT, VK_RSHIFT
            or 0xA2 or 0xA3               // VK_LCONTROL, VK_RCONTROL
            or 0xA4 or 0xA5               // VK_LMENU, VK_RMENU
            or 0x5B or 0x5C               // VK_LWIN, VK_RWIN
            or 0x14 or 0x90 or 0x91)      // CAPSLOCK, NUMLOCK, SCROLL
        {
            return false;
        }

        // WM_KEYDOWN dispatches:
        //   1. Ctrl+anything — no useful WM_CHAR for these
        //   2. Alt+anything — no useful WM_CHAR
        //   3. Non-char keys (arrows, F-keys, Escape, Ins, Del, etc.)
        //
        // Everything else (printable chars, Enter, Tab, Backspace) is
        // left for WM_CHAR which gives us the correct Unicode character
        // from the OS keyboard layout.

        if (ctrl || alt || IsNonCharKey(vk))
        {
            var consoleKey = VkToConsoleKey(vk);
            char ch = '\0';

            if (ctrl && vk >= 0x41 && vk <= 0x5A) // Ctrl+A..Z
                ch = (char)(vk - 0x41 + 1);       // Control chars 0x01-0x1A
            else if (!ctrl && !alt)
                ch = VkToChar(vk, shift);

            var ki = new ConsoleKeyInfo(ch, consoleKey, shift, alt, ctrl);
            KeyPressed?.Invoke(ki);
            return true; // Dispatched — swallow the message
        }

        // Printable key: let WM_CHAR handle it
        return false;
    }

    private void HandleChar(IntPtr wParam)
    {
        char ch = (char)wParam.ToInt32();

        // Ignore null chars
        if (ch == '\0') return;

        // Ignore control characters that were already dispatched by
        // HandleKeyDown. The message pump calls TranslateMessage
        // BEFORE DispatchMessage (which calls WndProc), so Ctrl+key
        // combinations generate a WM_CHAR with the control code
        // (e.g. Ctrl+B → ch=0x02) that's already queued by the time
        // we swallow the WM_KEYDOWN. We must filter these ghost chars.
        //
        // We keep only \r (Enter), \t (Tab), \b (Backspace) because
        // those are NOT handled in HandleKeyDown — they're left for
        // WM_CHAR on purpose as normal typing keys.
        // Escape (0x1B) IS handled in HandleKeyDown via IsNonCharKey,
        // so its WM_CHAR must also be filtered.
        if (ch < ' ' && ch != '\r' && ch != '\t' && ch != '\b')
            return;

        bool shift = (ModifierKeys & Keys.Shift) != 0;
        bool ctrl  = (ModifierKeys & Keys.Control) != 0;
        bool alt   = (ModifierKeys & Keys.Alt) != 0;

        ConsoleKey consoleKey;

        // Map the character to ConsoleKey
        if (ch == '\r')
            consoleKey = ConsoleKey.Enter;
        else if (ch == '\t')
            consoleKey = ConsoleKey.Tab;
        else if (ch == '\b')
            consoleKey = ConsoleKey.Backspace;
        else
            consoleKey = CharToConsoleKey(ch);

        var ki = new ConsoleKeyInfo(ch, consoleKey, shift, alt, ctrl);
        KeyPressed?.Invoke(ki);
    }

    /// <summary>
    /// Returns true for virtual-key codes that never generate a useful WM_CHAR.
    /// </summary>
    private static bool IsNonCharKey(int vk) => vk switch
    {
        0x1B => true,  // VK_ESCAPE
        0x21 => true,  // VK_PRIOR (Page Up)
        0x22 => true,  // VK_NEXT (Page Down)
        0x23 => true,  // VK_END
        0x24 => true,  // VK_HOME
        0x25 => true,  // VK_LEFT
        0x26 => true,  // VK_UP
        0x27 => true,  // VK_RIGHT
        0x28 => true,  // VK_DOWN
        0x2D => true,  // VK_INSERT
        0x2E => true,  // VK_DELETE
        >= 0x70 and <= 0x7B => true, // F1-F12
        _ => false
    };

    // ── Ensure arrow/tab/etc come through as input keys ─────

    protected override bool IsInputKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        return key switch
        {
            Keys.Up or Keys.Down or Keys.Left or Keys.Right
                or Keys.Tab or Keys.Escape or Keys.Enter
                => true,
            _ => base.IsInputKey(keyData)
        };
    }

    // Prevent WinForms from eating keys before they reach WndProc
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Return false so the message reaches our WndProc
        return false;
    }

    // ── Resize handling ─────────────────────────────────────

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (WindowState == FormWindowState.Minimized) return;

        int newCols = Math.Max(1, ClientSize.Width / _cellWidth);
        int newRows = Math.Max(1, ClientSize.Height / _cellHeight);

        if (newCols != _cols || newRows != _rows)
        {
            _cols = newCols;
            _rows = newRows;

            lock (_bufferLock)
            {
                _chars = new char[_rows, _cols];
                _fg = new ConsoleColor[_rows, _cols];
                _bg = new ConsoleColor[_rows, _cols];
                ClearGrid();
            }

            TerminalResized?.Invoke(_cols, _rows);
        }

        Invalidate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        WindowClosed?.Invoke();
        base.OnFormClosing(e);
    }

    // ── Painting ────────────────────────────────────────────

    private static readonly TextFormatFlags _textFlags =
        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding
        | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        // Determine which rows/columns are in the clip region to avoid
        // painting the entire grid when only a small area is dirty.
        var clip = e.ClipRectangle;
        int xStart = Math.Max(0, clip.Left / _cellWidth);
        int yStart = Math.Max(0, clip.Top / _cellHeight);

        lock (_bufferLock)
        {
            int rows = _chars.GetLength(0);
            int cols = _chars.GetLength(1);

            int xEnd = Math.Min(cols - 1, clip.Right / _cellWidth);
            int yEnd = Math.Min(rows - 1, clip.Bottom / _cellHeight);

            var cellSize = new Size(_cellWidth, _cellHeight);

            for (int y = yStart; y <= yEnd; y++)
            {
                int py = y * _cellHeight;

                // Batch background fills: merge consecutive cells with the same bg color
                int x = xStart;
                while (x <= xEnd)
                {
                    var bgIdx = (int)_bg[y, x];
                    int runStart = x;
                    while (x <= xEnd && (int)_bg[y, x] == bgIdx)
                        x++;

                    g.FillRectangle(_brushCache[bgIdx],
                        runStart * _cellWidth, py,
                        (x - runStart) * _cellWidth, _cellHeight);
                }

                // Cursor block
                if (_cursorVisible && y == _cursorRow
                    && _cursorCol >= xStart && _cursorCol <= xEnd && !_cursorBlink)
                {
                    g.FillRectangle(_cursorBrush,
                        _cursorCol * _cellWidth, py, _cellWidth, _cellHeight);
                }

                // Characters
                for (x = xStart; x <= xEnd; x++)
                {
                    char ch = _chars[y, x];
                    if (ch <= ' ') continue;

                    var fgColor = ConsoleColorToColor(_fg[y, x]);
                    if (_cursorVisible && y == _cursorRow && x == _cursorCol && !_cursorBlink)
                        fgColor = Color.Black;

                    var pt = new Point(x * _cellWidth, py);
                    TextRenderer.DrawText(g, ch.ToString(), _font, pt, fgColor, _textFlags);
                }
            }
        }
    }

    // ── Private helpers ─────────────────────────────────────

    private void ClearGrid()
    {
        int rows = _chars.GetLength(0);
        int cols = _chars.GetLength(1);
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                _chars[y, x] = ' ';
                _fg[y, x] = ConsoleColor.Gray;
                _bg[y, x] = ConsoleColor.Black;
            }
    }

    private void InvalidateCursorCell()
    {
        if (IsHandleCreated && !IsDisposed)
        {
            try
            {
                BeginInvoke(() =>
                {
                    int px = _cursorCol * _cellWidth;
                    int py = _cursorRow * _cellHeight;
                    Invalidate(new Rectangle(px, py, _cellWidth, _cellHeight));
                });
            }
            catch { }
        }
    }

    private void MeasureCellSize()
    {
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var size = g.MeasureString("M", _font, 0, StringFormat.GenericTypographic);
        _cellWidth = Math.Max(1, (int)Math.Ceiling(size.Width));
        _cellHeight = Math.Max(1, (int)Math.Ceiling(_font.GetHeight(g)));
    }

    private static Font FindMonospaceFont(float size)
    {
        string[] candidates = ["Cascadia Mono", "Consolas", "Courier New", "Lucida Console"];
        foreach (var name in candidates)
        {
            var f = new Font(name, size, FontStyle.Regular, GraphicsUnit.Point);
            if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return f;
            f.Dispose();
        }
        return new Font(FontFamily.GenericMonospace, size, FontStyle.Regular, GraphicsUnit.Point);
    }

    // ── VK → ConsoleKey mapping ─────────────────────────────

    private static ConsoleKey VkToConsoleKey(int vk) => vk switch
    {
        0x08 => ConsoleKey.Backspace,
        0x09 => ConsoleKey.Tab,
        0x0D => ConsoleKey.Enter,
        0x1B => ConsoleKey.Escape,
        0x20 => ConsoleKey.Spacebar,
        0x21 => ConsoleKey.PageUp,
        0x22 => ConsoleKey.PageDown,
        0x23 => ConsoleKey.End,
        0x24 => ConsoleKey.Home,
        0x25 => ConsoleKey.LeftArrow,
        0x26 => ConsoleKey.UpArrow,
        0x27 => ConsoleKey.RightArrow,
        0x28 => ConsoleKey.DownArrow,
        0x2D => ConsoleKey.Insert,
        0x2E => ConsoleKey.Delete,
        >= 0x30 and <= 0x39 => (ConsoleKey)(ConsoleKey.D0 + (vk - 0x30)),
        >= 0x41 and <= 0x5A => (ConsoleKey)(ConsoleKey.A + (vk - 0x41)),
        >= 0x60 and <= 0x69 => (ConsoleKey)(ConsoleKey.NumPad0 + (vk - 0x60)),
        0x6A => ConsoleKey.Multiply,
        0x6B => ConsoleKey.Add,
        0x6D => ConsoleKey.Subtract,
        0x6E => ConsoleKey.Decimal,
        0x6F => ConsoleKey.Divide,
        >= 0x70 and <= 0x7B => (ConsoleKey)(ConsoleKey.F1 + (vk - 0x70)),
        0xBA => ConsoleKey.Oem1,
        0xBB => ConsoleKey.OemPlus,
        0xBC => ConsoleKey.OemComma,
        0xBD => ConsoleKey.OemMinus,
        0xBE => ConsoleKey.OemPeriod,
        0xBF => ConsoleKey.Oem2,
        0xC0 => ConsoleKey.Oem3,
        0xDB => ConsoleKey.Oem4,
        0xDC => ConsoleKey.Oem5,
        0xDD => ConsoleKey.Oem6,
        0xDE => ConsoleKey.Oem7,
        _ => (ConsoleKey)vk
    };

    private static char VkToChar(int vk, bool shift) => vk switch
    {
        0x20 => ' ',
        0x0D => '\r',
        0x09 => '\t',
        0x08 => '\b',
        >= 0x41 and <= 0x5A => shift ? (char)vk : (char)(vk + 32),
        0x30 => shift ? ')' : '0',
        0x31 => shift ? '!' : '1',
        0x32 => shift ? '@' : '2',
        0x33 => shift ? '#' : '3',
        0x34 => shift ? '$' : '4',
        0x35 => shift ? '%' : '5',
        0x36 => shift ? '^' : '6',
        0x37 => shift ? '&' : '7',
        0x38 => shift ? '*' : '8',
        0x39 => shift ? '(' : '9',
        0xBA => shift ? ':' : ';',
        0xBB => shift ? '+' : '=',
        0xBC => shift ? '<' : ',',
        0xBD => shift ? '_' : '-',
        0xBE => shift ? '>' : '.',
        0xBF => shift ? '?' : '/',
        0xC0 => shift ? '~' : '`',
        0xDB => shift ? '{' : '[',
        0xDC => shift ? '|' : '\\',
        0xDD => shift ? '}' : ']',
        0xDE => shift ? '"' : '\'',
        _ => '\0'
    };

    private static ConsoleKey CharToConsoleKey(char c) => c switch
    {
        >= 'a' and <= 'z' => (ConsoleKey)(ConsoleKey.A + (c - 'a')),
        >= 'A' and <= 'Z' => (ConsoleKey)(ConsoleKey.A + (c - 'A')),
        >= '0' and <= '9' => (ConsoleKey)(ConsoleKey.D0 + (c - '0')),
        ' ' => ConsoleKey.Spacebar,
        '\r' or '\n' => ConsoleKey.Enter,
        '\t' => ConsoleKey.Tab,
        '\b' => ConsoleKey.Backspace,
        ';' or ':' => ConsoleKey.Oem1,
        '=' or '+' => ConsoleKey.OemPlus,
        ',' or '<' => ConsoleKey.OemComma,
        '-' or '_' => ConsoleKey.OemMinus,
        '.' or '>' => ConsoleKey.OemPeriod,
        '/' or '?' => ConsoleKey.Oem2,
        '`' or '~' => ConsoleKey.Oem3,
        '[' or '{' => ConsoleKey.Oem4,
        '\\' or '|' => ConsoleKey.Oem5,
        ']' or '}' => ConsoleKey.Oem6,
        '\'' or '"' => ConsoleKey.Oem7,
        '!' => ConsoleKey.D1,
        '@' => ConsoleKey.D2,
        '#' => ConsoleKey.D3,
        '$' => ConsoleKey.D4,
        '%' => ConsoleKey.D5,
        '^' => ConsoleKey.D6,
        '&' => ConsoleKey.D7,
        '*' => ConsoleKey.D8,
        '(' => ConsoleKey.D9,
        ')' => ConsoleKey.D0,
        _ => 0
    };

    private static Color ConsoleColorToColor(ConsoleColor c) => c switch
    {
        ConsoleColor.Black => Color.FromArgb(12, 12, 12),
        ConsoleColor.DarkBlue => Color.FromArgb(0, 55, 218),
        ConsoleColor.DarkGreen => Color.FromArgb(19, 161, 14),
        ConsoleColor.DarkCyan => Color.FromArgb(58, 150, 221),
        ConsoleColor.DarkRed => Color.FromArgb(197, 15, 31),
        ConsoleColor.DarkMagenta => Color.FromArgb(136, 23, 152),
        ConsoleColor.DarkYellow => Color.FromArgb(193, 156, 0),
        ConsoleColor.Gray => Color.FromArgb(204, 204, 204),
        ConsoleColor.DarkGray => Color.FromArgb(118, 118, 118),
        ConsoleColor.Blue => Color.FromArgb(59, 120, 255),
        ConsoleColor.Green => Color.FromArgb(22, 198, 12),
        ConsoleColor.Cyan => Color.FromArgb(97, 214, 214),
        ConsoleColor.Red => Color.FromArgb(231, 72, 86),
        ConsoleColor.Magenta => Color.FromArgb(180, 0, 158),
        ConsoleColor.Yellow => Color.FromArgb(249, 241, 165),
        ConsoleColor.White => Color.FromArgb(242, 242, 242),
        _ => Color.FromArgb(204, 204, 204)
    };

}
