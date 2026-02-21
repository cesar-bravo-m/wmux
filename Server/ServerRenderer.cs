using Wmux.Core;
using StatusBar = Wmux.UI.StatusBar;
using PaneBorder = Wmux.UI.PaneBorder;

namespace Wmux.Server;

/// <summary>
/// Server-side grid compositor. Builds a character grid and packs it into
/// a ScreenSnapshotMessage for sending over IPC to clients.
/// </summary>
public static class ServerRenderer
{
    public static ScreenSnapshotMessage BuildSnapshot(Session session, int width, int height,
        string? commandInput = null)
    {
        var activeWin = session.ActiveWindow;
        var panes = activeWin.GetPanes();
        var activePane = activeWin.ActivePane;

        int usableHeight = height - 1; // reserve 1 for status bar

        // Build the character grid
        var chars = new char[height, width];
        var fg = new ConsoleColor[height, width];
        var bg = new ConsoleColor[height, width];

        // Fill with spaces
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                chars[y, x] = ' ';
                fg[y, x] = ConsoleColor.Gray;
                bg[y, x] = ConsoleColor.Black;
            }

        PaneBorder.DrawBorders(chars, fg, panes, activePane, width, usableHeight);
        foreach (var pane in panes)
        {
            RenderPaneToGrid(pane, chars, fg, bg, pane.Left, pane.Top,
                pane.Width, pane.Height, height);
        }

        // Render status bar on the last row
        string statusStr = StatusBar.RenderPlain(session, width, commandInput);
        for (int x = 0; x < Math.Min(statusStr.Length, width); x++)
        {
            chars[height - 1, x] = statusStr[x];
            fg[height - 1, x] = ConsoleColor.Black;
            bg[height - 1, x] = ConsoleColor.Green;
        }
        for (int x = statusStr.Length; x < width; x++)
        {
            chars[height - 1, x] = ' ';
            fg[height - 1, x] = ConsoleColor.Black;
            bg[height - 1, x] = ConsoleColor.Green;
        }

        // Cursor position
        int cursorRow = activePane.Top + activePane.Screen.CursorRow;
        int cursorCol = activePane.Left + activePane.Screen.CursorCol;

        // Pack into message
        return new ScreenSnapshotMessage
        {
            Width = width,
            Height = height,
            Chars = FlattenChars(chars, height, width),
            Fg = FlattenColors(fg, height, width),
            Bg = FlattenColors(bg, height, width),
            CursorRow = cursorRow,
            CursorCol = cursorCol,
            CursorVisible = activePane.Screen.CursorVisible,
        };
    }

    private static void RenderPaneToGrid(Pane pane, char[,] chars, ConsoleColor[,] fg,
        ConsoleColor[,] bg, int destLeft, int destTop, int destWidth, int destHeight,
        int totalHeight)
    {
        pane.Lock(screen =>
        {
            for (int y = 0; y < Math.Min(destHeight, screen.Height); y++)
            {
                int screenY = destTop + y;
                if (screenY >= totalHeight - 1) break; // don't overwrite status bar

                for (int x = 0; x < Math.Min(destWidth, screen.Width); x++)
                {
                    int screenX = destLeft + x;
                    if (screenX >= chars.GetLength(1)) break;

                    chars[screenY, screenX] = screen.Chars[y][x];
                    fg[screenY, screenX] = screen.FgColors[y][x];
                    bg[screenY, screenX] = screen.BgColors[y][x];
                }
            }
        });
    }

    private static string FlattenChars(char[,] chars, int rows, int cols)
    {
        var sb = new char[rows * cols];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
                sb[y * cols + x] = chars[y, x];
        return new string(sb);
    }

    private static byte[] FlattenColors(ConsoleColor[,] colors, int rows, int cols)
    {
        var bytes = new byte[rows * cols];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
                bytes[y * cols + x] = (byte)colors[y, x];
        return bytes;
    }
}
