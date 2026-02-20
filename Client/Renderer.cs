using System.Text;
using Wmux.Core;
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

    public void Render(Session session, string? commandInput = null)
    {
        var window = session.ActiveWindow;
        var panes = window.GetPanes();
        var activePane = window.ActivePane;

        // Usable height (reserve 1 row for status bar)
        int usableHeight = _height - 1;

        // Build the character grid
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

        // Draw pane borders
        PaneBorder.DrawBorders(chars, fg, panes, activePane, _width, usableHeight);

        // Render each pane's content
        foreach (var pane in panes)
        {
            RenderPaneToGrid(pane, chars, fg, bg, pane.Left, pane.Top, pane.Width, pane.Height);
        }

        // Build the output using differential updates
        var sb = new StringBuilder(4096);

        // Hide cursor during render
        sb.Append("\x1b[?25l");

        bool fullRedraw = _prevChars == null || _prevChars.GetLength(0) != _height || _prevChars.GetLength(1) != _width;

        if (fullRedraw)
        {
            // Full screen redraw
            sb.Append("\x1b[2J"); // Clear screen
            for (int y = 0; y < usableHeight; y++)
            {
                sb.Append($"\x1b[{y + 1};1H"); // Move to row
                ConsoleColor curFg = ConsoleColor.Gray, curBg = ConsoleColor.Black;
                for (int x = 0; x < _width; x++)
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
            // Differential update
            for (int y = 0; y < usableHeight; y++)
            {
                for (int x = 0; x < _width; x++)
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

        // Render status bar on the last row
        sb.Append($"\x1b[{_height};1H");
        sb.Append(StatusBar.Render(session, _width, commandInput));

        // Position cursor at the active pane's cursor position
        int cursorScreenRow = activePane.Top + activePane.Screen.CursorRow + 1;
        int cursorScreenCol = activePane.Left + activePane.Screen.CursorCol + 1;

        sb.Append($"\x1b[{cursorScreenRow};{cursorScreenCol}H");

        // Show cursor
        if (activePane.Screen.CursorVisible)
            sb.Append("\x1b[?25h");

        // Save current buffer for next diff
        _prevChars = chars;
        _prevFg = fg;
        _prevBg = bg;

        // Flush all at once
        Console.Write(sb.ToString());
    }

    private void RenderPaneToGrid(Pane pane, char[,] chars, ConsoleColor[,] fg, ConsoleColor[,] bg,
        int destLeft, int destTop, int destWidth, int destHeight)
    {
        pane.Lock(screen =>
        {
            for (int y = 0; y < Math.Min(destHeight, screen.Height); y++)
            {
                int screenY = destTop + y;
                if (screenY >= _height - 1) break; // Don't overwrite status bar

                for (int x = 0; x < Math.Min(destWidth, screen.Width); x++)
                {
                    int screenX = destLeft + x;
                    if (screenX >= _width) break;

                    chars[screenY, screenX] = screen.Chars[y][x];
                    fg[screenY, screenX] = screen.FgColors[y][x];
                    bg[screenY, screenX] = screen.BgColors[y][x];
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
