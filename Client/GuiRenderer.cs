using Wmux.Core;
using Wmux.Terminal;
using StatusBar = Wmux.UI.StatusBar;
using PaneBorder = Wmux.UI.PaneBorder;

namespace Wmux.Client;

/// <summary>
/// Renders the wmux session to a TerminalWindow (GUI) instead of to
/// stdout via ANSI sequences. Builds the same character grid as the
/// old Renderer but pushes it to TerminalWindow.UpdateGrid().
/// </summary>
public class GuiRenderer
{
    private int _width;
    private int _height;

    public GuiRenderer(int width, int height)
    {
        _width = width;
        _height = height;
    }

    public void Resize(int width, int height)
    {
        _width = width;
        _height = height;
    }

    public void Render(Session session, TerminalWindow window, string? commandInput = null,
        ConsoleColor statusFg = ConsoleColor.Black, ConsoleColor statusBg = ConsoleColor.Green,
        bool commandMode = false)
    {
        var activeWin = session.ActiveWindow;
        var panes = activeWin.GetPanes();
        var activePane = activeWin.ActivePane;

        int usableHeight = _height - 1; // reserve 1 for status bar

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
        // Fill rest of status bar
        for (int x = statusStr.Length; x < _width; x++)
        {
            chars[_height - 1, x] = ' ';
            fg[_height - 1, x] = statusFg;
            bg[_height - 1, x] = statusBg;
        }

        // Command mode: draw a non-blinking black block cursor on the status bar
        if (commandMode)
        {
            int cmdCursorX = 1 + (commandInput?.Length ?? 0); // after ":" + input
            if (cmdCursorX < _width)
            {
                bg[_height - 1, cmdCursorX] = ConsoleColor.Black;
                fg[_height - 1, cmdCursorX] = statusBg;
            }
        }

        // Cursor position
        int cursorRow = activePane.Top + activePane.Screen.CursorRow;
        int cursorCol = activePane.Left + activePane.Screen.CursorCol;

        // In command mode, hide the regular blinking cursor (the grid cell IS the cursor)
        window.UpdateGrid(chars, fg, bg, cursorRow, cursorCol,
            commandMode ? false : activePane.Screen.CursorVisible);
    }

    private void RenderPaneToGrid(Pane pane, char[,] chars, ConsoleColor[,] fg, ConsoleColor[,] bg,
        int destLeft, int destTop, int destWidth, int destHeight)
    {
        pane.Lock(screen =>
        {
            for (int y = 0; y < Math.Min(destHeight, screen.Height); y++)
            {
                int screenY = destTop + y;
                if (screenY >= _height - 1) break;

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
}
