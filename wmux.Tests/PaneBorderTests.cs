using Wmux.Core;
using Wmux.UI;

namespace Wmux.Tests;

[TestFixture]
[Platform("Win")]
public class PaneBorderTests
{
    [Test]
    public void DrawBorders_SinglePane_NoBordersDrawn()
    {
        int w = 20, h = 10;
        var grid = new char[h, w];
        var colors = new ConsoleColor[h, w];

        using var pane = CreatePane(0, 0, w, h);
        var panes = new List<Pane> { pane };

        PaneBorder.DrawBorders(grid, colors, panes, pane, w, h);

        // Single pane should not have any border characters
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Assert.That(grid[y, x], Is.Not.EqualTo('│'));
    }

    [Test]
    public void DrawBorders_TwoPanesVertical_HasVerticalBorder()
    {
        int w = 21, h = 10;
        var grid = new char[h, w];
        var colors = new ConsoleColor[h, w];
        // Initialize
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                grid[y, x] = ' ';

        using var pane1 = CreatePane(0, 0, 10, h);
        using var pane2 = CreatePane(11, 0, 10, h);
        var panes = new List<Pane> { pane1, pane2 };

        PaneBorder.DrawBorders(grid, colors, panes, pane1, w, h);

        // Should have vertical border at column 10
        bool hasVertical = false;
        for (int y = 0; y < h; y++)
        {
            if (grid[y, 10] == '│')
                hasVertical = true;
        }
        Assert.That(hasVertical, Is.True);
    }

    [Test]
    public void DrawBorders_TwoPanesHorizontal_HasHorizontalBorder()
    {
        int w = 20, h = 11;
        var grid = new char[h, w];
        var colors = new ConsoleColor[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                grid[y, x] = ' ';

        using var pane1 = CreatePane(0, 0, w, 5);
        using var pane2 = CreatePane(0, 6, w, 5);
        var panes = new List<Pane> { pane1, pane2 };

        PaneBorder.DrawBorders(grid, colors, panes, pane1, w, h);

        // Should have horizontal border at row 5
        bool hasHorizontal = false;
        for (int x = 0; x < w; x++)
        {
            if (grid[5, x] == '─')
                hasHorizontal = true;
        }
        Assert.That(hasHorizontal, Is.True);
    }

    [Test]
    public void DrawBorders_ActivePaneBorderColor_IsGreen()
    {
        int w = 21, h = 10;
        var grid = new char[h, w];
        var colors = new ConsoleColor[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                grid[y, x] = ' ';

        using var pane1 = CreatePane(0, 0, 10, h);
        using var pane2 = CreatePane(11, 0, 10, h);
        var panes = new List<Pane> { pane1, pane2 };

        PaneBorder.DrawBorders(grid, colors, panes, pane1, w, h);

        // Active pane's border should be green
        Assert.That(colors[0, 10], Is.EqualTo(ConsoleColor.Green));
    }

    [Test]
    public void DrawBorders_InactivePaneBorderColor_IsDarkGray()
    {
        int w = 21, h = 10;
        var grid = new char[h, w];
        var colors = new ConsoleColor[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                grid[y, x] = ' ';

        using var pane1 = CreatePane(0, 0, 10, h);
        using var pane2 = CreatePane(11, 0, 10, h);
        var panes = new List<Pane> { pane1, pane2 };

        // pane2 is active, so pane1 border should be gray
        PaneBorder.DrawBorders(grid, colors, panes, pane2, w, h);

        Assert.That(colors[0, 10], Is.EqualTo(ConsoleColor.DarkGray));
    }

    private static Pane CreatePane(int left, int top, int width, int height)
    {
        var pane = new Pane(left, top, width, height);
        // Don't call Start() - we don't want to read from process in tests
        return pane;
    }
}
