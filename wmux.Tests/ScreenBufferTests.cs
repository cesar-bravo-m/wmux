using Wmux.Terminal;

namespace Wmux.Tests;

[TestFixture]
public class ScreenBufferTests
{
    private ScreenBuffer _buf = null!;

    [SetUp]
    public void SetUp()
    {
        _buf = new ScreenBuffer(80, 24);
    }

    // ── Constructor ──────────────────────────────────────────────────

    [Test]
    public void Constructor_InitializesCorrectDimensions()
    {
        Assert.That(_buf.Width, Is.EqualTo(80));
        Assert.That(_buf.Height, Is.EqualTo(24));
    }

    [Test]
    public void Constructor_CursorStartsAtOrigin()
    {
        Assert.That(_buf.CursorRow, Is.EqualTo(0));
        Assert.That(_buf.CursorCol, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_CursorIsVisibleByDefault()
    {
        Assert.That(_buf.CursorVisible, Is.True);
    }

    [Test]
    public void Constructor_AllCellsAreSpaces()
    {
        for (int r = 0; r < _buf.Height; r++)
            for (int c = 0; c < _buf.Width; c++)
                Assert.That(_buf.Chars[r][c], Is.EqualTo(' '));
    }

    [Test]
    public void Constructor_DefaultColorsAreGrayOnBlack()
    {
        Assert.That(_buf.FgColors[0][0], Is.EqualTo(ConsoleColor.Gray));
        Assert.That(_buf.BgColors[0][0], Is.EqualTo(ConsoleColor.Black));
    }

    // ── PutChar ──────────────────────────────────────────────────────

    [Test]
    public void PutChar_WritesCharacterAtCursor()
    {
        _buf.PutChar('A');
        Assert.That(_buf.Chars[0][0], Is.EqualTo('A'));
    }

    [Test]
    public void PutChar_AdvancesCursorColumn()
    {
        _buf.PutChar('A');
        Assert.That(_buf.CursorCol, Is.EqualTo(1));
    }

    [Test]
    public void PutChar_MultipleCalls_WritesSequentially()
    {
        _buf.PutChar('H');
        _buf.PutChar('i');
        Assert.That(_buf.Chars[0][0], Is.EqualTo('H'));
        Assert.That(_buf.Chars[0][1], Is.EqualTo('i'));
        Assert.That(_buf.CursorCol, Is.EqualTo(2));
    }

    [Test]
    public void PutChar_AtEndOfLine_WrapsToNextLine()
    {
        var buf = new ScreenBuffer(5, 3);
        for (int i = 0; i < 5; i++)
            buf.PutChar((char)('A' + i));

        // Cursor is now at col 5 (past end)
        // Next PutChar should wrap
        buf.PutChar('F');
        Assert.That(buf.Chars[1][0], Is.EqualTo('F'));
        Assert.That(buf.CursorRow, Is.EqualTo(1));
    }

    // ── CarriageReturn ───────────────────────────────────────────────

    [Test]
    public void CarriageReturn_MovesCursorToColumnZero()
    {
        _buf.PutChar('A');
        _buf.PutChar('B');
        _buf.CarriageReturn();
        Assert.That(_buf.CursorCol, Is.EqualTo(0));
        Assert.That(_buf.CursorRow, Is.EqualTo(0));
    }

    // ── LineFeed ─────────────────────────────────────────────────────

    [Test]
    public void LineFeed_MovesCursorDown()
    {
        _buf.LineFeed();
        Assert.That(_buf.CursorRow, Is.EqualTo(1));
    }

    [Test]
    public void LineFeed_AtBottomOfScreen_ScrollsUp()
    {
        var buf = new ScreenBuffer(10, 3);
        buf.MoveCursor(0, 0); buf.PutChar('A');
        buf.MoveCursor(1, 0); buf.PutChar('B');
        buf.MoveCursor(2, 0); buf.PutChar('C');

        buf.MoveCursor(2, 0);
        buf.LineFeed(); // Should scroll: A disappears, B→row0, C→row1, row2 blank

        Assert.That(buf.Chars[0][0], Is.EqualTo('B'));
        Assert.That(buf.Chars[1][0], Is.EqualTo('C'));
        Assert.That(buf.Chars[2][0], Is.EqualTo(' '));
    }

    // ── ReverseIndex ─────────────────────────────────────────────────

    [Test]
    public void ReverseIndex_MovesCursorUp()
    {
        _buf.MoveCursor(5, 0);
        _buf.ReverseIndex();
        Assert.That(_buf.CursorRow, Is.EqualTo(4));
    }

    [Test]
    public void ReverseIndex_AtTop_ScrollsDown()
    {
        var buf = new ScreenBuffer(10, 3);
        buf.MoveCursor(0, 0); buf.PutChar('A');
        buf.MoveCursor(1, 0); buf.PutChar('B');

        buf.MoveCursor(0, 0);
        buf.ReverseIndex(); // Should scroll down: row0 blank, A→row1, B→row2

        Assert.That(buf.Chars[0][0], Is.EqualTo(' '));
        Assert.That(buf.Chars[1][0], Is.EqualTo('A'));
        Assert.That(buf.Chars[2][0], Is.EqualTo('B'));
    }

    // ── Backspace ────────────────────────────────────────────────────

    [Test]
    public void Backspace_MovesCursorLeft()
    {
        _buf.PutChar('A');
        _buf.PutChar('B');
        _buf.Backspace();
        Assert.That(_buf.CursorCol, Is.EqualTo(1));
    }

    [Test]
    public void Backspace_AtColumnZero_StaysAtZero()
    {
        _buf.Backspace();
        Assert.That(_buf.CursorCol, Is.EqualTo(0));
    }

    // ── Tab ──────────────────────────────────────────────────────────

    [Test]
    public void Tab_MovesToNextTabStop()
    {
        _buf.Tab();
        Assert.That(_buf.CursorCol, Is.EqualTo(8));
    }

    [Test]
    public void Tab_FromColumn3_GoesToColumn8()
    {
        _buf.MoveCursor(0, 3);
        _buf.Tab();
        Assert.That(_buf.CursorCol, Is.EqualTo(8));
    }

    [Test]
    public void Tab_FromColumn8_GoesToColumn16()
    {
        _buf.MoveCursor(0, 8);
        _buf.Tab();
        Assert.That(_buf.CursorCol, Is.EqualTo(16));
    }

    // ── MoveCursor ───────────────────────────────────────────────────

    [Test]
    public void MoveCursor_SetsPosition()
    {
        _buf.MoveCursor(5, 10);
        Assert.That(_buf.CursorRow, Is.EqualTo(5));
        Assert.That(_buf.CursorCol, Is.EqualTo(10));
    }

    [Test]
    public void MoveCursor_ClampsToScreenBounds()
    {
        _buf.MoveCursor(100, 200);
        Assert.That(_buf.CursorRow, Is.EqualTo(23));
        Assert.That(_buf.CursorCol, Is.EqualTo(79));
    }

    [Test]
    public void MoveCursor_ClampsNegativeToZero()
    {
        _buf.MoveCursor(-5, -3);
        Assert.That(_buf.CursorRow, Is.EqualTo(0));
        Assert.That(_buf.CursorCol, Is.EqualTo(0));
    }

    // ── Directional Cursor Movement ──────────────────────────────────

    [Test]
    public void MoveCursorUp_DecrementsRow()
    {
        _buf.MoveCursor(5, 0);
        _buf.MoveCursorUp(3);
        Assert.That(_buf.CursorRow, Is.EqualTo(2));
    }

    [Test]
    public void MoveCursorDown_IncrementsRow()
    {
        _buf.MoveCursorDown(3);
        Assert.That(_buf.CursorRow, Is.EqualTo(3));
    }

    [Test]
    public void MoveCursorForward_IncrementsCol()
    {
        _buf.MoveCursorForward(5);
        Assert.That(_buf.CursorCol, Is.EqualTo(5));
    }

    [Test]
    public void MoveCursorBackward_DecrementsCol()
    {
        _buf.MoveCursor(0, 10);
        _buf.MoveCursorBackward(3);
        Assert.That(_buf.CursorCol, Is.EqualTo(7));
    }

    [Test]
    public void MoveCursorUp_ClampsAtTop()
    {
        _buf.MoveCursorUp(100);
        Assert.That(_buf.CursorRow, Is.EqualTo(0));
    }

    [Test]
    public void MoveCursorForward_ClampsAtRight()
    {
        _buf.MoveCursorForward(200);
        Assert.That(_buf.CursorCol, Is.EqualTo(79));
    }

    // ── EraseInDisplay ───────────────────────────────────────────────

    [Test]
    public void EraseInDisplay_Mode0_ClearsFromCursorToEnd()
    {
        for (int i = 0; i < 10; i++) _buf.PutChar('X');
        _buf.MoveCursor(0, 5);
        _buf.EraseInDisplay(0);

        // Characters before cursor should remain
        for (int i = 0; i < 5; i++)
            Assert.That(_buf.Chars[0][i], Is.EqualTo('X'));
        // Characters from cursor onward should be blank
        for (int i = 5; i < 10; i++)
            Assert.That(_buf.Chars[0][i], Is.EqualTo(' '));
    }

    [Test]
    public void EraseInDisplay_Mode1_ClearsFromStartToCursor()
    {
        for (int i = 0; i < 10; i++) _buf.PutChar('X');
        _buf.MoveCursor(0, 5);
        _buf.EraseInDisplay(1);

        for (int i = 0; i <= 5; i++)
            Assert.That(_buf.Chars[0][i], Is.EqualTo(' '));
        for (int i = 6; i < 10; i++)
            Assert.That(_buf.Chars[0][i], Is.EqualTo('X'));
    }

    [Test]
    public void EraseInDisplay_Mode2_ClearsEntireScreen()
    {
        for (int i = 0; i < 10; i++) _buf.PutChar('X');
        _buf.EraseInDisplay(2);

        for (int r = 0; r < _buf.Height; r++)
            for (int c = 0; c < _buf.Width; c++)
                Assert.That(_buf.Chars[r][c], Is.EqualTo(' '));
    }

    // ── EraseInLine ──────────────────────────────────────────────────

    [Test]
    public void EraseInLine_Mode0_ClearsFromCursorToEndOfLine()
    {
        for (int i = 0; i < 10; i++) _buf.PutChar('X');
        _buf.MoveCursor(0, 5);
        _buf.EraseInLine(0);

        for (int i = 0; i < 5; i++)
            Assert.That(_buf.Chars[0][i], Is.EqualTo('X'));
        for (int i = 5; i < _buf.Width; i++)
            Assert.That(_buf.Chars[0][i], Is.EqualTo(' '));
    }

    [Test]
    public void EraseInLine_Mode2_ClearsEntireLine()
    {
        for (int i = 0; i < 10; i++) _buf.PutChar('X');
        _buf.MoveCursor(0, 5);
        _buf.EraseInLine(2);

        for (int c = 0; c < _buf.Width; c++)
            Assert.That(_buf.Chars[0][c], Is.EqualTo(' '));
    }

    // ── InsertLines / DeleteLines ────────────────────────────────────

    [Test]
    public void InsertLines_ShiftsLinesDown()
    {
        var buf = new ScreenBuffer(10, 5);
        buf.MoveCursor(0, 0); buf.PutChar('A');
        buf.MoveCursor(1, 0); buf.PutChar('B');
        buf.MoveCursor(2, 0); buf.PutChar('C');

        buf.MoveCursor(1, 0);
        buf.InsertLines(1);

        Assert.That(buf.Chars[0][0], Is.EqualTo('A'));
        Assert.That(buf.Chars[1][0], Is.EqualTo(' ')); // Inserted blank
        Assert.That(buf.Chars[2][0], Is.EqualTo('B'));
        Assert.That(buf.Chars[3][0], Is.EqualTo('C'));
    }

    [Test]
    public void DeleteLines_ShiftsLinesUp()
    {
        var buf = new ScreenBuffer(10, 5);
        buf.MoveCursor(0, 0); buf.PutChar('A');
        buf.MoveCursor(1, 0); buf.PutChar('B');
        buf.MoveCursor(2, 0); buf.PutChar('C');

        buf.MoveCursor(1, 0);
        buf.DeleteLines(1);

        Assert.That(buf.Chars[0][0], Is.EqualTo('A'));
        Assert.That(buf.Chars[1][0], Is.EqualTo('C'));
    }

    // ── DeleteChars / InsertChars / EraseChars ───────────────────────

    [Test]
    public void DeleteChars_ShiftsCharsLeft()
    {
        _buf.PutChar('A'); _buf.PutChar('B'); _buf.PutChar('C'); _buf.PutChar('D');
        _buf.MoveCursor(0, 1);
        _buf.DeleteChars(1);

        Assert.That(_buf.Chars[0][0], Is.EqualTo('A'));
        Assert.That(_buf.Chars[0][1], Is.EqualTo('C'));
        Assert.That(_buf.Chars[0][2], Is.EqualTo('D'));
    }

    [Test]
    public void InsertChars_ShiftsCharsRight()
    {
        _buf.PutChar('A'); _buf.PutChar('B'); _buf.PutChar('C');
        _buf.MoveCursor(0, 1);
        _buf.InsertChars(1);

        Assert.That(_buf.Chars[0][0], Is.EqualTo('A'));
        Assert.That(_buf.Chars[0][1], Is.EqualTo(' ')); // Inserted blank
        Assert.That(_buf.Chars[0][2], Is.EqualTo('B'));
        Assert.That(_buf.Chars[0][3], Is.EqualTo('C'));
    }

    [Test]
    public void EraseChars_BlanksCharactersWithoutShifting()
    {
        _buf.PutChar('A'); _buf.PutChar('B'); _buf.PutChar('C'); _buf.PutChar('D');
        _buf.MoveCursor(0, 1);
        _buf.EraseChars(2);

        Assert.That(_buf.Chars[0][0], Is.EqualTo('A'));
        Assert.That(_buf.Chars[0][1], Is.EqualTo(' '));
        Assert.That(_buf.Chars[0][2], Is.EqualTo(' '));
        Assert.That(_buf.Chars[0][3], Is.EqualTo('D'));
    }

    // ── Scroll Region ────────────────────────────────────────────────

    [Test]
    public void SetScrollRegion_DefinesRegion()
    {
        var buf = new ScreenBuffer(10, 10);
        buf.MoveCursor(1, 0); buf.PutChar('A');
        buf.MoveCursor(2, 0); buf.PutChar('B');
        buf.MoveCursor(3, 0); buf.PutChar('C');
        buf.MoveCursor(4, 0); buf.PutChar('D');

        buf.SetScrollRegion(1, 3);
        buf.MoveCursor(3, 0);
        buf.LineFeed(); // Should scroll within region (rows 1-3)

        // Row 0 should be unchanged
        Assert.That(buf.Chars[1][0], Is.EqualTo('B'));
        Assert.That(buf.Chars[2][0], Is.EqualTo('C'));
        Assert.That(buf.Chars[3][0], Is.EqualTo(' ')); // Scrolled out
    }

    // ── SaveCursor / RestoreCursor ───────────────────────────────────

    [Test]
    public void SaveAndRestoreCursor_RestoresPosition()
    {
        _buf.MoveCursor(5, 10);
        _buf.SaveCursor();
        _buf.MoveCursor(0, 0);
        _buf.RestoreCursor();

        Assert.That(_buf.CursorRow, Is.EqualTo(5));
        Assert.That(_buf.CursorCol, Is.EqualTo(10));
    }

    // ── SetGraphicsRendition ─────────────────────────────────────────

    [Test]
    public void SGR_Reset_SetsDefaultColors()
    {
        _buf.SetGraphicsRendition([31]); // Red fg
        _buf.SetGraphicsRendition([0]);  // Reset
        _buf.PutChar('A');
        Assert.That(_buf.FgColors[0][0], Is.EqualTo(ConsoleColor.Gray));
        Assert.That(_buf.BgColors[0][0], Is.EqualTo(ConsoleColor.Black));
    }

    [Test]
    public void SGR_SetsForegroundColor()
    {
        _buf.SetGraphicsRendition([32]); // Green
        _buf.PutChar('A');
        Assert.That(_buf.FgColors[0][0], Is.EqualTo(ConsoleColor.DarkGreen));
    }

    [Test]
    public void SGR_SetsBackgroundColor()
    {
        _buf.SetGraphicsRendition([41]); // Red bg
        _buf.PutChar('A');
        Assert.That(_buf.BgColors[0][0], Is.EqualTo(ConsoleColor.DarkRed));
    }

    [Test]
    public void SGR_SetsBold()
    {
        _buf.SetGraphicsRendition([1]);
        _buf.PutChar('A');
        Assert.That(_buf.Bold[0][0], Is.True);
    }

    [Test]
    public void SGR_BrightColors()
    {
        _buf.SetGraphicsRendition([92]); // Bright green
        _buf.PutChar('A');
        Assert.That(_buf.FgColors[0][0], Is.EqualTo(ConsoleColor.Green));
    }

    [Test]
    public void SGR_EmptyParams_Resets()
    {
        _buf.SetGraphicsRendition([31]);
        _buf.SetGraphicsRendition([]); // Should reset when empty passed
        // ScreenBuffer treats empty array input as reset (caller passes [0] for no params)
        _buf.PutChar('A');
        Assert.That(_buf.FgColors[0][0], Is.EqualTo(ConsoleColor.Gray));
    }

    // ── Resize ───────────────────────────────────────────────────────

    [Test]
    public void Resize_PreservesExistingContent()
    {
        _buf.PutChar('A');
        _buf.PutChar('B');
        _buf.Resize(40, 12);

        Assert.That(_buf.Width, Is.EqualTo(40));
        Assert.That(_buf.Height, Is.EqualTo(12));
        Assert.That(_buf.Chars[0][0], Is.EqualTo('A'));
        Assert.That(_buf.Chars[0][1], Is.EqualTo('B'));
    }

    [Test]
    public void Resize_ClampsCursorPosition()
    {
        _buf.MoveCursor(20, 70);
        _buf.Resize(10, 5);

        Assert.That(_buf.CursorRow, Is.LessThan(5));
        Assert.That(_buf.CursorCol, Is.LessThan(10));
    }

    [Test]
    public void Resize_GrowingAddsBlankRows()
    {
        var buf = new ScreenBuffer(10, 3);
        buf.PutChar('X');
        buf.Resize(10, 6);

        Assert.That(buf.Height, Is.EqualTo(6));
        Assert.That(buf.Chars[0][0], Is.EqualTo('X'));
        Assert.That(buf.Chars[5][0], Is.EqualTo(' '));
    }

    // ── ScrollUp / ScrollDown ────────────────────────────────────────

    [Test]
    public void ScrollUp_ShiftsContentUp()
    {
        var buf = new ScreenBuffer(10, 3);
        buf.MoveCursor(0, 0); buf.PutChar('A');
        buf.MoveCursor(1, 0); buf.PutChar('B');
        buf.MoveCursor(2, 0); buf.PutChar('C');

        buf.ScrollUp(1);

        Assert.That(buf.Chars[0][0], Is.EqualTo('B'));
        Assert.That(buf.Chars[1][0], Is.EqualTo('C'));
        Assert.That(buf.Chars[2][0], Is.EqualTo(' '));
    }

    [Test]
    public void ScrollDown_ShiftsContentDown()
    {
        var buf = new ScreenBuffer(10, 3);
        buf.MoveCursor(0, 0); buf.PutChar('A');
        buf.MoveCursor(1, 0); buf.PutChar('B');
        buf.MoveCursor(2, 0); buf.PutChar('C');

        buf.ScrollDown(1);

        Assert.That(buf.Chars[0][0], Is.EqualTo(' '));
        Assert.That(buf.Chars[1][0], Is.EqualTo('A'));
        Assert.That(buf.Chars[2][0], Is.EqualTo('B'));
    }
}
