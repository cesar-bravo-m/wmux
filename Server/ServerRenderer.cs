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
        string statusStr = activePane.IsInSelectionMode
            ? StatusBar.RenderPlain(session, width, "selection mode")
            : StatusBar.RenderPlain(session, width, commandInput);
        var statusBarFg = activePane.IsInSelectionMode ? ConsoleColor.Black : ConsoleColor.Black;
        var statusBarBg = activePane.IsInSelectionMode ? ConsoleColor.Yellow : ConsoleColor.Green;
        for (int x = 0; x < Math.Min(statusStr.Length, width); x++)
        {
            chars[height - 1, x] = statusStr[x];
            fg[height - 1, x] = statusBarFg;
            bg[height - 1, x] = statusBarBg;
        }
        for (int x = statusStr.Length; x < width; x++)
        {
            chars[height - 1, x] = ' ';
            fg[height - 1, x] = statusBarFg;
            bg[height - 1, x] = statusBarBg;
        }

        // Cursor position
        int cursorRow, cursorCol;
        bool cursorVisible;
        if (activePane.IsInSelectionMode)
        {
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
                        if (screenY >= 0 && screenY < height - 1 && screenX >= 0 && screenX < width)
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
                if (cursorRow >= 0 && cursorRow < height - 1 && cursorCol >= 0 && cursorCol < width)
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
            cursorVisible = activePane.Screen.CursorVisible;
        }

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
            CursorVisible = cursorVisible,
        };
    }

    private static void RenderPaneToGrid(Pane pane, char[,] chars, ConsoleColor[,] fg,
        ConsoleColor[,] bg, int destLeft, int destTop, int destWidth, int destHeight,
        int totalHeight)
    {
        pane.Lock(screen =>
        {
            int scrollOffset = pane.IsInSelectionMode ? pane.SelectionScrollOffset : 0;
            int scrollbackCount = pane.Scrollback.Count;

            for (int y = 0; y < Math.Min(destHeight, screen.Height); y++)
            {
                int screenY = destTop + y;
                if (screenY >= totalHeight - 1) break; // don't overwrite status bar

                if (scrollOffset > 0)
                {
                    int virtualLine = scrollbackCount - scrollOffset + y;
                    for (int x = 0; x < Math.Min(destWidth, screen.Width); x++)
                    {
                        int screenX = destLeft + x;
                        if (screenX >= chars.GetLength(1)) break;

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
                        if (screenX >= chars.GetLength(1)) break;

                        chars[screenY, screenX] = screen.Chars[y][x];
                        fg[screenY, screenX] = screen.FgColors[y][x];
                        bg[screenY, screenX] = screen.BgColors[y][x];
                    }
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
