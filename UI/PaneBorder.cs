using System.Text;
using Wmux.Core;

namespace Wmux.UI;

/// <summary>
/// Draws pane borders using Unicode box-drawing characters.
/// </summary>
public static class PaneBorder
{
    // Box drawing characters
    private const char Horizontal = '─';
    private const char Vertical = '│';
    private const char TopLeft = '┌';
    private const char TopRight = '┐';
    private const char BottomLeft = '└';
    private const char BottomRight = '┘';
    private const char TeeLeft = '├';
    private const char TeeRight = '┤';
    private const char TeeTop = '┬';
    private const char TeeBottom = '┴';
    private const char Cross = '┼';

    /// <summary>
    /// Renders border lines between panes into a character grid.
    /// Border segments adjacent to the active pane are highlighted green.
    /// </summary>
    public static void DrawBorders(char[,] grid, ConsoleColor[,] colorGrid, List<Pane> panes, Pane activePane, int totalWidth, int totalHeight)
    {
        // Pass 1: Draw all border characters in default color
        foreach (var pane in panes)
        {
            // Right border (vertical line to the right of this pane)
            int rightEdge = pane.Left + pane.Width;
            if (rightEdge < totalWidth)
            {
                for (int y = pane.Top; y < pane.Top + pane.Height && y < totalHeight; y++)
                {
                    if (grid[y, rightEdge] == '\0' || grid[y, rightEdge] == ' ')
                    {
                        grid[y, rightEdge] = Vertical;
                        colorGrid[y, rightEdge] = ConsoleColor.DarkGray;
                    }
                    else if (grid[y, rightEdge] == Horizontal)
                    {
                        grid[y, rightEdge] = Cross;
                    }
                }
            }

            // Bottom border (horizontal line below this pane)
            int bottomEdge = pane.Top + pane.Height;
            if (bottomEdge < totalHeight)
            {
                for (int x = pane.Left; x < pane.Left + pane.Width && x < totalWidth; x++)
                {
                    if (grid[bottomEdge, x] == '\0' || grid[bottomEdge, x] == ' ')
                    {
                        grid[bottomEdge, x] = Horizontal;
                        colorGrid[bottomEdge, x] = ConsoleColor.DarkGray;
                    }
                    else if (grid[bottomEdge, x] == Vertical)
                    {
                        grid[bottomEdge, x] = Cross;
                    }
                }
            }

            // Corner at bottom-right
            if (rightEdge < totalWidth && bottomEdge < totalHeight)
            {
                grid[bottomEdge, rightEdge] = Cross;
                colorGrid[bottomEdge, rightEdge] = ConsoleColor.DarkGray;
            }
        }

        // Pass 2: Highlight border segments adjacent to the active pane in green
        int aLeft = activePane.Left;
        int aTop = activePane.Top;
        int aRight = aLeft + activePane.Width;
        int aBottom = aTop + activePane.Height;

        // Left edge of active pane (vertical separator at column aLeft - 1)
        if (aLeft > 0)
        {
            int x = aLeft - 1;
            for (int y = aTop; y < aBottom && y < totalHeight; y++)
                if (IsBorderChar(grid[y, x]))
                    colorGrid[y, x] = ConsoleColor.Green;
        }

        // Right edge of active pane (vertical separator at column aRight)
        if (aRight < totalWidth)
        {
            for (int y = aTop; y < aBottom && y < totalHeight; y++)
                if (IsBorderChar(grid[y, aRight]))
                    colorGrid[y, aRight] = ConsoleColor.Green;
        }

        // Top edge of active pane (horizontal separator at row aTop - 1)
        if (aTop > 0)
        {
            int y = aTop - 1;
            for (int x = aLeft; x < aRight && x < totalWidth; x++)
                if (IsBorderChar(grid[y, x]))
                    colorGrid[y, x] = ConsoleColor.Green;
        }

        // Bottom edge of active pane (horizontal separator at row aBottom)
        if (aBottom < totalHeight)
        {
            for (int x = aLeft; x < aRight && x < totalWidth; x++)
                if (IsBorderChar(grid[aBottom, x]))
                    colorGrid[aBottom, x] = ConsoleColor.Green;
        }

        // Corners where active pane borders meet
        // Top-left corner
        if (aLeft > 0 && aTop > 0 && IsBorderChar(grid[aTop - 1, aLeft - 1]))
            colorGrid[aTop - 1, aLeft - 1] = ConsoleColor.Green;
        // Top-right corner
        if (aRight < totalWidth && aTop > 0 && IsBorderChar(grid[aTop - 1, aRight]))
            colorGrid[aTop - 1, aRight] = ConsoleColor.Green;
        // Bottom-left corner
        if (aLeft > 0 && aBottom < totalHeight && IsBorderChar(grid[aBottom, aLeft - 1]))
            colorGrid[aBottom, aLeft - 1] = ConsoleColor.Green;
        // Bottom-right corner
        if (aRight < totalWidth && aBottom < totalHeight && IsBorderChar(grid[aBottom, aRight]))
            colorGrid[aBottom, aRight] = ConsoleColor.Green;
    }

    private static bool IsBorderChar(char c) =>
        c == Horizontal || c == Vertical || c == Cross ||
        c == TopLeft || c == TopRight || c == BottomLeft || c == BottomRight ||
        c == TeeLeft || c == TeeRight || c == TeeTop || c == TeeBottom;
}
