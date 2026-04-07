using Wmux.Terminal;

namespace Wmux.Tests;

[TestFixture]
public class VtParserTests
{
    private ScreenBuffer _screen = null!;
    private VtParser _parser = null!;

    [SetUp]
    public void SetUp()
    {
        _screen = new ScreenBuffer(80, 24);
        _parser = new VtParser();
    }

    private void Parse(string data) => _parser.Process(_screen, data.AsSpan());

    // ── Basic character output ───────────────────────────────────────

    [Test]
    public void PlainText_WrittenToScreen()
    {
        Parse("Hello");
        Assert.That(_screen.Chars[0][0], Is.EqualTo('H'));
        Assert.That(_screen.Chars[0][4], Is.EqualTo('o'));
        Assert.That(_screen.CursorCol, Is.EqualTo(5));
    }

    [Test]
    public void ControlCharacters_CarriageReturn()
    {
        Parse("AB\rC");
        Assert.That(_screen.Chars[0][0], Is.EqualTo('C'));
        Assert.That(_screen.Chars[0][1], Is.EqualTo('B'));
    }

    [Test]
    public void ControlCharacters_LineFeed()
    {
        Parse("A\nB");
        Assert.That(_screen.Chars[0][0], Is.EqualTo('A'));
        Assert.That(_screen.Chars[1][1], Is.EqualTo('B'));
    }

    [Test]
    public void ControlCharacters_Backspace()
    {
        Parse("AB\bC");
        Assert.That(_screen.Chars[0][0], Is.EqualTo('A'));
        Assert.That(_screen.Chars[0][1], Is.EqualTo('C'));
    }

    [Test]
    public void ControlCharacters_Tab()
    {
        Parse("A\tB");
        Assert.That(_screen.Chars[0][0], Is.EqualTo('A'));
        Assert.That(_screen.Chars[0][8], Is.EqualTo('B'));
    }

    [Test]
    public void ControlCharacters_BellIgnored()
    {
        Parse("A\x07" + "B");
        Assert.That(_screen.Chars[0][0], Is.EqualTo('A'));
        Assert.That(_screen.Chars[0][1], Is.EqualTo('B'));
    }

    [Test]
    public void NullCharacter_Ignored()
    {
        Parse("A\0B");
        Assert.That(_screen.Chars[0][0], Is.EqualTo('A'));
        Assert.That(_screen.Chars[0][1], Is.EqualTo('B'));
    }

    // ── CSI Cursor Movement ──────────────────────────────────────────

    [Test]
    public void CSI_CursorUp()
    {
        _screen.MoveCursor(5, 0);
        Parse("\x1b[3A"); // Move up 3
        Assert.That(_screen.CursorRow, Is.EqualTo(2));
    }

    [Test]
    public void CSI_CursorDown()
    {
        Parse("\x1b[5B"); // Move down 5
        Assert.That(_screen.CursorRow, Is.EqualTo(5));
    }

    [Test]
    public void CSI_CursorForward()
    {
        Parse("\x1b[10C"); // Move forward 10
        Assert.That(_screen.CursorCol, Is.EqualTo(10));
    }

    [Test]
    public void CSI_CursorBackward()
    {
        _screen.MoveCursor(0, 10);
        Parse("\x1b[3D"); // Move backward 3
        Assert.That(_screen.CursorCol, Is.EqualTo(7));
    }

    [Test]
    public void CSI_CursorPosition()
    {
        Parse("\x1b[10;20H"); // Move to row 10, col 20 (1-based)
        Assert.That(_screen.CursorRow, Is.EqualTo(9));
        Assert.That(_screen.CursorCol, Is.EqualTo(19));
    }

    [Test]
    public void CSI_CursorPosition_DefaultParams()
    {
        _screen.MoveCursor(10, 10);
        Parse("\x1b[H"); // Default to 1;1
        Assert.That(_screen.CursorRow, Is.EqualTo(0));
        Assert.That(_screen.CursorCol, Is.EqualTo(0));
    }

    [Test]
    public void CSI_CursorHorizontalAbsolute()
    {
        Parse("\x1b[15G"); // Column 15 (1-based)
        Assert.That(_screen.CursorCol, Is.EqualTo(14));
    }

    [Test]
    public void CSI_CursorVerticalAbsolute()
    {
        Parse("\x1b[8d"); // Row 8 (1-based)
        Assert.That(_screen.CursorRow, Is.EqualTo(7));
    }

    [Test]
    public void CSI_CursorNextLine()
    {
        _screen.MoveCursor(0, 10);
        Parse("\x1b[2E"); // 2 lines down, col 0
        Assert.That(_screen.CursorRow, Is.EqualTo(2));
        Assert.That(_screen.CursorCol, Is.EqualTo(0));
    }

    [Test]
    public void CSI_CursorPreviousLine()
    {
        _screen.MoveCursor(5, 10);
        Parse("\x1b[2F"); // 2 lines up, col 0
        Assert.That(_screen.CursorRow, Is.EqualTo(3));
        Assert.That(_screen.CursorCol, Is.EqualTo(0));
    }

    // ── CSI Erase ────────────────────────────────────────────────────

    [Test]
    public void CSI_EraseInDisplay()
    {
        Parse("ABCDE");
        Parse("\x1b[2J"); // Clear screen
        for (int c = 0; c < _screen.Width; c++)
            Assert.That(_screen.Chars[0][c], Is.EqualTo(' '));
    }

    [Test]
    public void CSI_EraseInLine()
    {
        Parse("ABCDE");
        _screen.MoveCursor(0, 2);
        Parse("\x1b[K"); // Erase from cursor to end of line
        Assert.That(_screen.Chars[0][0], Is.EqualTo('A'));
        Assert.That(_screen.Chars[0][1], Is.EqualTo('B'));
        Assert.That(_screen.Chars[0][2], Is.EqualTo(' '));
        Assert.That(_screen.Chars[0][3], Is.EqualTo(' '));
    }

    // ── CSI Delete/Insert ────────────────────────────────────────────

    [Test]
    public void CSI_DeleteCharacters()
    {
        Parse("ABCD");
        _screen.MoveCursor(0, 1);
        Parse("\x1b[1P"); // Delete 1 char
        Assert.That(_screen.Chars[0][0], Is.EqualTo('A'));
        Assert.That(_screen.Chars[0][1], Is.EqualTo('C'));
    }

    [Test]
    public void CSI_InsertCharacters()
    {
        Parse("ABCD");
        _screen.MoveCursor(0, 1);
        Parse("\x1b[1@"); // Insert 1 blank
        Assert.That(_screen.Chars[0][0], Is.EqualTo('A'));
        Assert.That(_screen.Chars[0][1], Is.EqualTo(' '));
        Assert.That(_screen.Chars[0][2], Is.EqualTo('B'));
    }

    [Test]
    public void CSI_InsertLines()
    {
        var screen = new ScreenBuffer(10, 5);
        var parser = new VtParser();
        screen.MoveCursor(0, 0); screen.PutChar('A');
        screen.MoveCursor(1, 0); screen.PutChar('B');
        screen.MoveCursor(1, 0);
        parser.Process(screen, "\x1b[1L".AsSpan()); // Insert 1 line
        Assert.That(screen.Chars[1][0], Is.EqualTo(' '));
        Assert.That(screen.Chars[2][0], Is.EqualTo('B'));
    }

    [Test]
    public void CSI_DeleteLines()
    {
        var screen = new ScreenBuffer(10, 5);
        var parser = new VtParser();
        screen.MoveCursor(0, 0); screen.PutChar('A');
        screen.MoveCursor(1, 0); screen.PutChar('B');
        screen.MoveCursor(2, 0); screen.PutChar('C');
        screen.MoveCursor(1, 0);
        parser.Process(screen, "\x1b[1M".AsSpan()); // Delete 1 line
        Assert.That(screen.Chars[1][0], Is.EqualTo('C'));
    }

    // ── CSI Scroll ───────────────────────────────────────────────────

    [Test]
    public void CSI_ScrollUp()
    {
        var screen = new ScreenBuffer(10, 3);
        var parser = new VtParser();
        screen.MoveCursor(0, 0); screen.PutChar('A');
        screen.MoveCursor(1, 0); screen.PutChar('B');
        parser.Process(screen, "\x1b[1S".AsSpan());
        Assert.That(screen.Chars[0][0], Is.EqualTo('B'));
    }

    [Test]
    public void CSI_ScrollDown()
    {
        var screen = new ScreenBuffer(10, 3);
        var parser = new VtParser();
        screen.MoveCursor(0, 0); screen.PutChar('A');
        screen.MoveCursor(1, 0); screen.PutChar('B');
        parser.Process(screen, "\x1b[1T".AsSpan());
        Assert.That(screen.Chars[0][0], Is.EqualTo(' '));
        Assert.That(screen.Chars[1][0], Is.EqualTo('A'));
    }

    // ── CSI Graphics Rendition (SGR) ─────────────────────────────────

    [Test]
    public void CSI_SGR_SetForeground()
    {
        Parse("\x1b[31mA"); // Red foreground
        Assert.That(_screen.FgColors[0][0], Is.EqualTo(ConsoleColor.DarkRed));
    }

    [Test]
    public void CSI_SGR_SetBackground()
    {
        Parse("\x1b[42mA"); // Green background
        Assert.That(_screen.BgColors[0][0], Is.EqualTo(ConsoleColor.DarkGreen));
    }

    [Test]
    public void CSI_SGR_Bold()
    {
        Parse("\x1b[1mA");
        Assert.That(_screen.Bold[0][0], Is.True);
    }

    [Test]
    public void CSI_SGR_Reset()
    {
        Parse("\x1b[31m\x1b[0mA");
        Assert.That(_screen.FgColors[0][0], Is.EqualTo(ConsoleColor.Gray));
    }

    [Test]
    public void CSI_SGR_MultipleParams()
    {
        Parse("\x1b[1;32;41mA"); // Bold, green fg, red bg
        Assert.That(_screen.Bold[0][0], Is.True);
        Assert.That(_screen.FgColors[0][0], Is.EqualTo(ConsoleColor.DarkGreen));
        Assert.That(_screen.BgColors[0][0], Is.EqualTo(ConsoleColor.DarkRed));
    }

    [Test]
    public void CSI_SGR_BrightForeground()
    {
        Parse("\x1b[91mA"); // Bright red
        Assert.That(_screen.FgColors[0][0], Is.EqualTo(ConsoleColor.Red));
    }

    [Test]
    public void CSI_SGR_BrightBackground()
    {
        Parse("\x1b[102mA"); // Bright green bg
        Assert.That(_screen.BgColors[0][0], Is.EqualTo(ConsoleColor.Green));
    }

    // ── CSI Scroll Region ────────────────────────────────────────────

    [Test]
    public void CSI_SetScrollRegion()
    {
        Parse("\x1b[3;10r"); // Set scroll region rows 3-10 (1-based)
        // After setting scroll region, cursor should be at 0,0
        Assert.That(_screen.CursorRow, Is.EqualTo(0));
        Assert.That(_screen.CursorCol, Is.EqualTo(0));
    }

    // ── CSI Save/Restore cursor ──────────────────────────────────────

    [Test]
    public void CSI_SaveRestoreCursor()
    {
        _screen.MoveCursor(5, 10);
        Parse("\x1b[s"); // Save
        _screen.MoveCursor(0, 0);
        Parse("\x1b[u"); // Restore
        Assert.That(_screen.CursorRow, Is.EqualTo(5));
        Assert.That(_screen.CursorCol, Is.EqualTo(10));
    }

    // ── CSI Erase Characters ─────────────────────────────────────────

    [Test]
    public void CSI_EraseCharacters()
    {
        Parse("ABCD");
        _screen.MoveCursor(0, 1);
        Parse("\x1b[2X"); // Erase 2 chars
        Assert.That(_screen.Chars[0][0], Is.EqualTo('A'));
        Assert.That(_screen.Chars[0][1], Is.EqualTo(' '));
        Assert.That(_screen.Chars[0][2], Is.EqualTo(' '));
        Assert.That(_screen.Chars[0][3], Is.EqualTo('D'));
    }

    // ── ESC sequences ────────────────────────────────────────────────

    [Test]
    public void ESC_SaveCursor()
    {
        _screen.MoveCursor(3, 7);
        Parse("\x1b" + "7"); // ESC 7 = save cursor
        _screen.MoveCursor(0, 0);
        Parse("\x1b" + "8"); // ESC 8 = restore cursor
        Assert.That(_screen.CursorRow, Is.EqualTo(3));
        Assert.That(_screen.CursorCol, Is.EqualTo(7));
    }

    [Test]
    public void ESC_ReverseIndex()
    {
        _screen.MoveCursor(5, 0);
        Parse("\x1bM"); // Reverse index
        Assert.That(_screen.CursorRow, Is.EqualTo(4));
    }

    [Test]
    public void ESC_Index_LineFeed()
    {
        _screen.MoveCursor(0, 5);
        Parse("\x1b" + "D"); // Index = line feed
        Assert.That(_screen.CursorRow, Is.EqualTo(1));
    }

    [Test]
    public void ESC_NextLine()
    {
        _screen.MoveCursor(0, 5);
        Parse("\x1b" + "E"); // Next line = CR + LF
        Assert.That(_screen.CursorRow, Is.EqualTo(1));
        Assert.That(_screen.CursorCol, Is.EqualTo(0));
    }

    [Test]
    public void ESC_Reset()
    {
        Parse("ABCDE");
        Parse("\x1b" + "c"); // Full reset
        Assert.That(_screen.CursorRow, Is.EqualTo(0));
        Assert.That(_screen.CursorCol, Is.EqualTo(0));
        for (int c = 0; c < _screen.Width; c++)
            Assert.That(_screen.Chars[0][c], Is.EqualTo(' '));
    }

    // ── OSC sequences ────────────────────────────────────────────────

    [Test]
    public void OSC_SetTitle()
    {
        Parse("\x1b]0;My Terminal\x07"); // OSC 0;title BEL
        Assert.That(_screen.Title, Is.EqualTo("My Terminal"));
    }

    [Test]
    public void OSC_SetTitle_Osc2()
    {
        Parse("\x1b]2;Window Title\x07");
        Assert.That(_screen.Title, Is.EqualTo("Window Title"));
    }

    // ── DEC Private Modes ────────────────────────────────────────────

    [Test]
    public void DecPrivateMode_HideCursor()
    {
        Parse("\x1b[?25l");
        Assert.That(_screen.CursorVisible, Is.False);
    }

    [Test]
    public void DecPrivateMode_ShowCursor()
    {
        _screen.CursorVisible = false;
        Parse("\x1b[?25h");
        Assert.That(_screen.CursorVisible, Is.True);
    }

    [Test]
    public void DecPrivateMode_AltScreenBuffer()
    {
        Parse("ABC");
        Parse("\x1b[?1049h"); // Switch to alt buffer (clears screen)
        Assert.That(_screen.CursorRow, Is.EqualTo(0));
        Assert.That(_screen.CursorCol, Is.EqualTo(0));
    }

    // ── Incremental / Chunked parsing ────────────────────────────────

    [Test]
    public void IncrementalParsing_SplitEscapeSequence()
    {
        Parse("\x1b");      // Just ESC
        Parse("[");         // Start CSI
        Parse("10;20H");   // Finish cursor position
        Assert.That(_screen.CursorRow, Is.EqualTo(9));
        Assert.That(_screen.CursorCol, Is.EqualTo(19));
    }

    [Test]
    public void IncrementalParsing_SplitSGR()
    {
        Parse("\x1b[3");
        Parse("1mA");
        Assert.That(_screen.FgColors[0][0], Is.EqualTo(ConsoleColor.DarkRed));
    }

    // ── Default parameters ───────────────────────────────────────────

    [Test]
    public void CSI_CursorUp_DefaultParam()
    {
        _screen.MoveCursor(5, 0);
        Parse("\x1b[A"); // No param = default 1
        Assert.That(_screen.CursorRow, Is.EqualTo(4));
    }

    [Test]
    public void CSI_CursorDown_DefaultParam()
    {
        Parse("\x1b[B"); // No param = default 1
        Assert.That(_screen.CursorRow, Is.EqualTo(1));
    }

    // ── Colon sub-parameters (SGR RGB) ────────────────────────────────

    [Test]
    public void ColonSgr_ForegroundRgb_WithColorSpace()
    {
        // \e[38:2::255:0:0m — colon-delimited RGB with empty color space ID
        Parse("\x1b[38:2::255:0:0mA");
        Assert.That(_screen.Chars[0][0], Is.EqualTo('A'));
        Assert.That(_screen.FgColors[0][0], Is.EqualTo(ConsoleColor.Red));
    }

    [Test]
    public void ColonSgr_ForegroundRgb_WithoutColorSpace()
    {
        // \e[38:2:0:255:0m — colon-delimited RGB without color space (5 params)
        Parse("\x1b[38:2:0:255:0mA");
        Assert.That(_screen.Chars[0][0], Is.EqualTo('A'));
        Assert.That(_screen.FgColors[0][0], Is.EqualTo(ConsoleColor.Green));
    }

    [Test]
    public void ColonSgr_BackgroundRgb_WithColorSpace()
    {
        // \e[48:2::0:0:255m — colon-delimited background RGB
        Parse("\x1b[48:2::0:0:255mA");
        Assert.That(_screen.Chars[0][0], Is.EqualTo('A'));
        Assert.That(_screen.BgColors[0][0], Is.EqualTo(ConsoleColor.Blue));
    }

    [Test]
    public void ColonSgr_DoesNotPrintSequenceAsText()
    {
        // The bug: colon params were printed as literal text
        Parse("\x1b[38:2::255:0:0mHello");
        Assert.That(_screen.Chars[0][0], Is.EqualTo('H'));
        Assert.That(_screen.Chars[0][4], Is.EqualTo('o'));
        Assert.That(_screen.CursorCol, Is.EqualTo(5));
    }
}
