using Wmux.Core;

namespace Wmux.Tests;

[TestFixture]
public class PaneSelectionTests
{
    /// <summary>
    /// Write known content into a pane's screen buffer for testing.
    /// </summary>
    private static void WriteScreenContent(Pane pane, string[] lines)
    {
        pane.Lock(screen =>
        {
            for (int r = 0; r < Math.Min(lines.Length, screen.Height); r++)
            {
                for (int c = 0; c < Math.Min(lines[r].Length, screen.Width); c++)
                {
                    screen.Chars[r][c] = lines[r][c];
                }
            }
        });
    }

    [Test]
    public void ExtractSelectedText_SingleLine()
    {
        var session = new Session("test", 40, 10);
        var pane = session.ActiveWindow.ActivePane;

        WriteScreenContent(pane, new[]
        {
            "Hello World this is a test line",
            "Second line of text",
        });

        // Enter selection mode, set cursor at row 0 col 0
        pane.EnterSelectionMode();
        pane.SelectionCursorRow = 0;
        pane.SelectionCursorCol = 0;

        // Start highlight at (0, 0)
        pane.StartSelectionHighlight();

        // Move cursor to (0, 4) — selects "Hello"
        pane.SelectionCursorCol = 4;

        string text = pane.ExtractSelectedText();
        Assert.That(text, Is.EqualTo("Hello"));
    }

    [Test]
    public void ExtractSelectedText_MultiLine()
    {
        var session = new Session("test", 40, 10);
        var pane = session.ActiveWindow.ActivePane;

        WriteScreenContent(pane, new[]
        {
            "Line zero here",
            "Line one here",
            "Line two here",
            "Line three here",
        });

        pane.EnterSelectionMode();
        pane.SelectionCursorRow = 1;
        pane.SelectionCursorCol = 5;

        // Start highlight at row 1, col 5
        pane.StartSelectionHighlight();

        // Move cursor to row 3, col 9 — selects partial first/last rows
        pane.SelectionCursorRow = 3;
        pane.SelectionCursorCol = 9;

        string text = pane.ExtractSelectedText();
        var lines = text.Split(Environment.NewLine);

        Assert.That(lines.Length, Is.EqualTo(3));
        Assert.That(lines[0], Is.EqualTo("one here"));       // row 1 from col 5
        Assert.That(lines[1], Is.EqualTo("Line two here"));   // row 2 full line
        Assert.That(lines[2], Is.EqualTo("Line three"));      // row 3 up to col 9
    }

    [Test]
    public void ExtractSelectedText_ReverseSelection()
    {
        var session = new Session("test", 40, 10);
        var pane = session.ActiveWindow.ActivePane;

        WriteScreenContent(pane, new[]
        {
            "ABCDEFGHIJ",
            "KLMNOPQRST",
        });

        pane.EnterSelectionMode();

        // Start highlight at row 1, col 5
        pane.SelectionCursorRow = 1;
        pane.SelectionCursorCol = 5;
        pane.StartSelectionHighlight();

        // Move cursor ABOVE the anchor (reverse selection) to row 0, col 2
        pane.SelectionCursorRow = 0;
        pane.SelectionCursorCol = 2;

        string text = pane.ExtractSelectedText();
        var lines = text.Split(Environment.NewLine);

        Assert.That(lines.Length, Is.EqualTo(2));
        Assert.That(lines[0], Is.EqualTo("CDEFGHIJ"));    // row 0 from col 2
        Assert.That(lines[1], Is.EqualTo("KLMNOP"));       // row 1 up to col 5
    }

    [Test]
    public void ExtractSelectedText_TrimsTrailingSpaces()
    {
        var session = new Session("test", 40, 10);
        var pane = session.ActiveWindow.ActivePane;

        // Screen buffer is initialized with spaces. Only write partial text.
        WriteScreenContent(pane, new[]
        {
            "Short",
        });

        pane.EnterSelectionMode();
        pane.SelectionCursorRow = 0;
        pane.SelectionCursorCol = 0;
        pane.StartSelectionHighlight();

        // Select the full row (0 to 39) — should trim trailing spaces
        pane.SelectionCursorCol = 39;

        string text = pane.ExtractSelectedText();
        Assert.That(text, Is.EqualTo("Short"));
    }

    [Test]
    public void ExtractSelectedText_WithScrollback()
    {
        var session = new Session("test", 20, 5);
        var pane = session.ActiveWindow.ActivePane;

        // Add lines to scrollback manually
        pane.Scrollback.Add(new ScrollbackLine(
            "Scrollback line 0  ".ToCharArray(),
            new ConsoleColor[20],
            new ConsoleColor[20]));
        pane.Scrollback.Add(new ScrollbackLine(
            "Scrollback line 1  ".ToCharArray(),
            new ConsoleColor[20],
            new ConsoleColor[20]));

        // Write screen content
        WriteScreenContent(pane, new[]
        {
            "Screen line 0",
            "Screen line 1",
        });

        pane.EnterSelectionMode();

        // Scroll up to see scrollback (offset 2 = 2 scrollback lines visible)
        pane.SelectionScrollOffset = 2;
        pane.SelectionCursorRow = 0; // This maps to scrollback line 0
        pane.SelectionCursorCol = 0;
        pane.StartSelectionHighlight();

        // Move to row 3 (maps to screen line 1) col 12
        pane.SelectionCursorRow = 3;
        pane.SelectionCursorCol = 12;

        string text = pane.ExtractSelectedText();
        var lines = text.Split(Environment.NewLine);

        Assert.That(lines.Length, Is.EqualTo(4));
        Assert.That(lines[0], Is.EqualTo("Scrollback line 0"));
        Assert.That(lines[1], Is.EqualTo("Scrollback line 1"));
        Assert.That(lines[2], Is.EqualTo("Screen line 0"));
        Assert.That(lines[3], Is.EqualTo("Screen line 1"));
    }

    [Test]
    public void ExtractSelectedText_SingleCharacter()
    {
        var session = new Session("test", 40, 10);
        var pane = session.ActiveWindow.ActivePane;

        WriteScreenContent(pane, new[]
        {
            "ABCDEF",
        });

        pane.EnterSelectionMode();
        pane.SelectionCursorRow = 0;
        pane.SelectionCursorCol = 2;
        pane.StartSelectionHighlight();

        // Cursor same as anchor — single character
        string text = pane.ExtractSelectedText();
        Assert.That(text, Is.EqualTo("C"));
    }

    [Test]
    public void StartSelectionHighlight_CapturesAnchorCoordinates()
    {
        var session = new Session("test", 40, 10);
        var pane = session.ActiveWindow.ActivePane;

        pane.EnterSelectionMode();
        pane.SelectionCursorRow = 3;
        pane.SelectionCursorCol = 7;
        pane.SelectionScrollOffset = 0;

        pane.StartSelectionHighlight();

        Assert.That(pane.SelectionHighlightActive, Is.True);
        Assert.That(pane.SelectionAnchorCol, Is.EqualTo(7));
        // Virtual row = scrollback.Count - scrollOffset + cursorRow = 0 - 0 + 3 = 3
        Assert.That(pane.SelectionAnchorVirtualRow, Is.EqualTo(3));
    }

    [Test]
    public void ExitSelectionMode_ResetsHighlightState()
    {
        var session = new Session("test", 40, 10);
        var pane = session.ActiveWindow.ActivePane;

        pane.EnterSelectionMode();
        pane.StartSelectionHighlight();
        Assert.That(pane.SelectionHighlightActive, Is.True);

        pane.ExitSelectionMode();
        Assert.That(pane.SelectionHighlightActive, Is.False);
        Assert.That(pane.IsInSelectionMode, Is.False);
    }
}
