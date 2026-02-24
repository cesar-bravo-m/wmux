using System.Text;
using Wmux.Core;
using Wmux.Server;
using StatusBar = Wmux.UI.StatusBar;
using PaneBorder = Wmux.UI.PaneBorder;

namespace Wmux.Client;

/// <summary>
/// ANSI-based screen renderer. Double-buffers the entire screen and
/// flushes to stdout in one write for flicker-free rendering.
/// </summary>
public class Renderer
{
    private int _width;
    private int _height;
    private char[,]? _prevChars;
    private ConsoleColor[,]? _prevFg;
    private ConsoleColor[,]? _prevBg;

    public Renderer(int width, int height)
    {
        _width = width;
        _height = height;
    }

    public void Resize(int width, int height)
    {
        _width = width;
        _height = height;
        _prevChars = null; // Force full redraw
    }

    /// <summary>
    /// Render a local Session to the terminal (standalone mode).
    /// </summary>
    public void Render(Session session, string? commandInput = null,
        ConsoleColor statusFg = ConsoleColor.Black, ConsoleColor statusBg = ConsoleColor.Green,
        bool commandMode = false)
    {
        var window = session.ActiveWindow;
        var panes = window.GetPanes();
        var activePane = window.ActivePane;

        int usableHeight = _height - 1;

        var chars = new char[_height, _width];
        var fg = new ConsoleColor[_height, _width];
        var bg = new ConsoleColor[_height, _width];

        // Fill with spaces
        for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
            {
                chars[y, x] = ' ';
                fg[y, x] = ConsoleColor.Gray;
                bg[y, x] = ConsoleColor.Black;
            }

        PaneBorder.DrawBorders(chars, fg, panes, activePane, _width, usableHeight);

        foreach (var pane in panes)
        {
            RenderPaneToGrid(pane, chars, fg, bg, pane.Left, pane.Top, pane.Width, pane.Height);
        }

        // Render status bar on the last row
        string statusStr = StatusBar.RenderPlain(session, _width, commandInput);
        for (int x = 0; x < Math.Min(statusStr.Length, _width); x++)
        {
            chars[_height - 1, x] = statusStr[x];
            fg[_height - 1, x] = statusFg;
            bg[_height - 1, x] = statusBg;
        }
        for (int x = statusStr.Length; x < _width; x++)
        {
            chars[_height - 1, x] = ' ';
            fg[_height - 1, x] = statusFg;
            bg[_height - 1, x] = statusBg;
        }

        // Command mode: draw a non-blinking block cursor on the status bar
        if (commandMode)
        {
            int cmdCursorX = 1 + (commandInput?.Length ?? 0);
            if (cmdCursorX < _width)
            {
                bg[_height - 1, cmdCursorX] = ConsoleColor.Black;
                fg[_height - 1, cmdCursorX] = statusBg;
            }
        }

        int cursorRow, cursorCol;
        bool cursorVisible;
        if (activePane.IsInSelectionMode)
        {
            // Selection mode: show cursor at selection position
            cursorRow = activePane.Top + activePane.SelectionCursorRow;
            cursorCol = activePane.Left + activePane.SelectionCursorCol;
            cursorVisible = true;

            if (activePane.SelectionHighlightActive)
            {
                // Invert all cells in the selection range
                int scrollbackCount = activePane.Scrollback.Count;
                int scrollOffset = activePane.SelectionScrollOffset;
                int anchorVR = activePane.SelectionAnchorVirtualRow;
                int cursorVR = scrollbackCount - scrollOffset + activePane.SelectionCursorRow;
                int anchorCol = activePane.SelectionAnchorCol;
                int curCursorCol = activePane.SelectionCursorCol;

                int startVR, startCol, endVR, endCol;
                if (anchorVR < cursorVR || (anchorVR == cursorVR && anchorCol <= curCursorCol))
                { startVR = anchorVR; startCol = anchorCol; endVR = cursorVR; endCol = curCursorCol; }
                else
                { startVR = cursorVR; startCol = curCursorCol; endVR = anchorVR; endCol = anchorCol; }

                for (int y = 0; y < activePane.Height; y++)
                {
                    int displayVR = scrollbackCount - scrollOffset + y;
                    if (displayVR < startVR || displayVR > endVR) continue;

                    int colStart = (displayVR == startVR) ? startCol : 0;
                    int colEnd = (displayVR == endVR) ? endCol : activePane.Width - 1;

                    for (int x = colStart; x <= colEnd && x < activePane.Width; x++)
                    {
                        int screenY = activePane.Top + y;
                        int screenX = activePane.Left + x;
                        if (screenY >= 0 && screenY < _height - 1 && screenX >= 0 && screenX < _width)
                        {
                            (fg[screenY, screenX], bg[screenY, screenX]) =
                                (bg[screenY, screenX], fg[screenY, screenX]);
                        }
                    }
                }
            }
            else
            {
                // No highlight — just invert the single cursor cell
                if (cursorRow >= 0 && cursorRow < _height - 1 && cursorCol >= 0 && cursorCol < _width)
                {
                    (fg[cursorRow, cursorCol], bg[cursorRow, cursorCol]) =
                        (bg[cursorRow, cursorCol], fg[cursorRow, cursorCol]);
                }
            }
        }
        else
        {
            cursorRow = activePane.Top + activePane.Screen.CursorRow;
            cursorCol = activePane.Left + activePane.Screen.CursorCol;
            cursorVisible = commandMode ? false : activePane.Screen.CursorVisible;
        }

        FlushToTerminal(chars, fg, bg, cursorRow, cursorCol, cursorVisible);
    }

    /// <summary>
    /// Render a ScreenSnapshotMessage from the server to the terminal (server mode).
    /// </summary>
    public void RenderSnapshot(ScreenSnapshotMessage snapshot,
        string? commandInput = null, string? statusOverlay = null,
        ConsoleColor statusFg = ConsoleColor.Black, ConsoleColor statusBg = ConsoleColor.Green,
        bool commandMode = false)
    {
        int w = snapshot.Width;
        int h = snapshot.Height;

        // Use the renderer's dimensions (actual terminal size)
        int renderW = _width;
        int renderH = _height;

        var chars = new char[renderH, renderW];
        var fg = new ConsoleColor[renderH, renderW];
        var bg = new ConsoleColor[renderH, renderW];

        // Fill with spaces
        for (int y = 0; y < renderH; y++)
            for (int x = 0; x < renderW; x++)
            {
                chars[y, x] = ' ';
                fg[y, x] = ConsoleColor.Gray;
                bg[y, x] = ConsoleColor.Black;
            }

        // Unpack the snapshot grid
        for (int y = 0; y < Math.Min(h, renderH); y++)
        {
            for (int x = 0; x < Math.Min(w, renderW); x++)
            {
                int idx = y * w + x;
                chars[y, x] = idx < snapshot.Chars.Length ? snapshot.Chars[idx] : ' ';
                fg[y, x] = idx < snapshot.Fg.Length ? (ConsoleColor)snapshot.Fg[idx] : ConsoleColor.Gray;
                bg[y, x] = idx < snapshot.Bg.Length ? (ConsoleColor)snapshot.Bg[idx] : ConsoleColor.Black;
            }
        }

        // Overlay command-line prompt if active (client-local state)
        if (commandMode && commandInput != null)
        {
            string cmdLine = $":{commandInput}";
            int statusRow = renderH - 1;
            if (statusRow >= 0)
            {
                for (int x = 0; x < renderW; x++)
                {
                    chars[statusRow, x] = x < cmdLine.Length ? cmdLine[x] : ' ';
                    fg[statusRow, x] = ConsoleColor.Black;
                    bg[statusRow, x] = ConsoleColor.Yellow;
                }
                // Non-blinking block cursor after typed text
                int cursorX = cmdLine.Length;
                if (cursorX < renderW)
                {
                    bg[statusRow, cursorX] = ConsoleColor.Black;
                    fg[statusRow, cursorX] = ConsoleColor.Yellow;
                }
            }
        }
        else if (statusOverlay != null)
        {
            int statusRow = renderH - 1;
            if (statusRow >= 0)
            {
                for (int x = 0; x < renderW; x++)
                {
                    chars[statusRow, x] = x < statusOverlay.Length ? statusOverlay[x] : ' ';
                    fg[statusRow, x] = statusFg;
                    bg[statusRow, x] = statusBg;
                }
            }
        }

        int cursorRow = snapshot.CursorRow;
        int cursorCol = snapshot.CursorCol;
        bool cursorVisible = commandMode ? false : snapshot.CursorVisible;

        FlushToTerminal(chars, fg, bg, cursorRow, cursorCol, cursorVisible);
    }

    /// <summary>
    /// Diff the current grid against the previous one and flush ANSI to stdout.
    /// </summary>
    private void FlushToTerminal(char[,] chars, ConsoleColor[,] fg, ConsoleColor[,] bg,
        int cursorRow, int cursorCol, bool cursorVisible)
    {
        int h = chars.GetLength(0);
        int w = chars.GetLength(1);

        var sb = new StringBuilder(4096);

        // Hide cursor during render
        sb.Append("\x1b[?25l");

        bool fullRedraw = _prevChars == null || _prevChars.GetLength(0) != h || _prevChars.GetLength(1) != w;

        if (fullRedraw)
        {
            sb.Append("\x1b[2J"); // Clear screen
            for (int y = 0; y < h; y++)
            {
                sb.Append($"\x1b[{y + 1};1H");
                ConsoleColor curFg = ConsoleColor.Gray, curBg = ConsoleColor.Black;
                for (int x = 0; x < w; x++)
                {
                    if (fg[y, x] != curFg || bg[y, x] != curBg)
                    {
                        curFg = fg[y, x];
                        curBg = bg[y, x];
                        sb.Append(AnsiColor(curFg, curBg));
                    }
                    sb.Append(chars[y, x]);
                }
                sb.Append("\x1b[0m");
            }
        }
        else
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (chars[y, x] != _prevChars![y, x] || fg[y, x] != _prevFg![y, x] || bg[y, x] != _prevBg![y, x])
                    {
                        sb.Append($"\x1b[{y + 1};{x + 1}H");
                        sb.Append(AnsiColor(fg[y, x], bg[y, x]));
                        sb.Append(chars[y, x]);
                    }
                }
            }
        }

        // Position cursor
        sb.Append($"\x1b[{cursorRow + 1};{cursorCol + 1}H");

        // Show/hide cursor
        if (cursorVisible)
            sb.Append("\x1b[?25h");

        sb.Append("\x1b[0m");

        // Save current buffer for next diff
        _prevChars = chars;
        _prevFg = fg;
        _prevBg = bg;

        Console.Write(sb.ToString());
    }

    private void RenderPaneToGrid(Pane pane, char[,] chars, ConsoleColor[,] fg, ConsoleColor[,] bg,
        int destLeft, int destTop, int destWidth, int destHeight)
    {
        pane.Lock(screen =>
        {
            int scrollOffset = pane.IsInSelectionMode ? pane.SelectionScrollOffset : 0;
            int scrollbackCount = pane.Scrollback.Count;

            for (int y = 0; y < Math.Min(destHeight, screen.Height); y++)
            {
                int screenY = destTop + y;
                if (screenY >= _height - 1) break;

                if (scrollOffset > 0)
                {
                    int virtualLine = scrollbackCount - scrollOffset + y;
                    for (int x = 0; x < Math.Min(destWidth, screen.Width); x++)
                    {
                        int screenX = destLeft + x;
                        if (screenX >= _width) break;

                        if (virtualLine < 0)
                        {
                            chars[screenY, screenX] = ' ';
                            fg[screenY, screenX] = ConsoleColor.Gray;
                            bg[screenY, screenX] = ConsoleColor.Black;
                        }
                        else if (virtualLine < scrollbackCount)
                        {
                            var line = pane.Scrollback.GetLine(virtualLine);
                            if (line.HasValue && x < line.Value.Chars.Length)
                            {
                                chars[screenY, screenX] = line.Value.Chars[x];
                                fg[screenY, screenX] = line.Value.Fg[x];
                                bg[screenY, screenX] = line.Value.Bg[x];
                            }
                            else
                            {
                                chars[screenY, screenX] = ' ';
                                fg[screenY, screenX] = ConsoleColor.Gray;
                                bg[screenY, screenX] = ConsoleColor.Black;
                            }
                        }
                        else
                        {
                            int srcRow = virtualLine - scrollbackCount;
                            if (srcRow < screen.Height)
                            {
                                chars[screenY, screenX] = screen.Chars[srcRow][x];
                                fg[screenY, screenX] = screen.FgColors[srcRow][x];
                                bg[screenY, screenX] = screen.BgColors[srcRow][x];
                            }
                        }
                    }
                }
                else
                {
                    for (int x = 0; x < Math.Min(destWidth, screen.Width); x++)
                    {
                        int screenX = destLeft + x;
                        if (screenX >= _width) break;

                        chars[screenY, screenX] = screen.Chars[y][x];
                        fg[screenY, screenX] = screen.FgColors[y][x];
                        bg[screenY, screenX] = screen.BgColors[y][x];
                    }
                }
            }
        });
    }

    private static string AnsiColor(ConsoleColor fg, ConsoleColor bg)
    {
        return $"\x1b[{ConsoleFgCode(fg)};{ConsoleBgCode(bg)}m";
    }

    private static int ConsoleFgCode(ConsoleColor c) => c switch
    {
        ConsoleColor.Black => 30,
        ConsoleColor.DarkRed => 31,
        ConsoleColor.DarkGreen => 32,
        ConsoleColor.DarkYellow => 33,
        ConsoleColor.DarkBlue => 34,
        ConsoleColor.DarkMagenta => 35,
        ConsoleColor.DarkCyan => 36,
        ConsoleColor.Gray => 37,
        ConsoleColor.DarkGray => 90,
        ConsoleColor.Red => 91,
        ConsoleColor.Green => 92,
        ConsoleColor.Yellow => 93,
        ConsoleColor.Blue => 94,
        ConsoleColor.Magenta => 95,
        ConsoleColor.Cyan => 96,
        ConsoleColor.White => 97,
        _ => 37
    };

    private static int ConsoleBgCode(ConsoleColor c) => c switch
    {
        ConsoleColor.Black => 40,
        ConsoleColor.DarkRed => 41,
        ConsoleColor.DarkGreen => 42,
        ConsoleColor.DarkYellow => 43,
        ConsoleColor.DarkBlue => 44,
        ConsoleColor.DarkMagenta => 45,
        ConsoleColor.DarkCyan => 46,
        ConsoleColor.Gray => 47,
        ConsoleColor.DarkGray => 100,
        ConsoleColor.Red => 101,
        ConsoleColor.Green => 102,
        ConsoleColor.Yellow => 103,
        ConsoleColor.Blue => 104,
        ConsoleColor.Magenta => 105,
        ConsoleColor.Cyan => 106,
        ConsoleColor.White => 107,
        _ => 40
    };
}
