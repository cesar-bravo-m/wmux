using Wmux.Client;
using Wmux.Config;

namespace Wmux.Tests;

[TestFixture]
public class InputHandlerTests
{
    // ── KeyToVtSequence ──────────────────────────────────────────────

    [Test]
    public void KeyToVt_Enter()
    {
        var key = new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\r"));
    }

    [Test]
    public void KeyToVt_Backspace()
    {
        var key = new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\x7f"));
    }

    [Test]
    public void KeyToVt_Tab()
    {
        var key = new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\t"));
    }

    [Test]
    public void KeyToVt_Escape()
    {
        var key = new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\x1b"));
    }

    [Test]
    public void KeyToVt_UpArrow()
    {
        var key = new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\x1b[A"));
    }

    [Test]
    public void KeyToVt_DownArrow()
    {
        var key = new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\x1b[B"));
    }

    [Test]
    public void KeyToVt_RightArrow()
    {
        var key = new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\x1b[C"));
    }

    [Test]
    public void KeyToVt_LeftArrow()
    {
        var key = new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\x1b[D"));
    }

    [Test]
    public void KeyToVt_Home()
    {
        var key = new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\x1b[H"));
    }

    [Test]
    public void KeyToVt_End()
    {
        var key = new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\x1b[F"));
    }

    [Test]
    public void KeyToVt_Delete()
    {
        var key = new ConsoleKeyInfo('\0', ConsoleKey.Delete, false, false, false);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\x1b[3~"));
    }

    [Test]
    public void KeyToVt_Insert()
    {
        var key = new ConsoleKeyInfo('\0', ConsoleKey.Insert, false, false, false);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\x1b[2~"));
    }

    [Test]
    public void KeyToVt_PageUp()
    {
        var key = new ConsoleKeyInfo('\0', ConsoleKey.PageUp, false, false, false);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\x1b[5~"));
    }

    [Test]
    public void KeyToVt_PageDown()
    {
        var key = new ConsoleKeyInfo('\0', ConsoleKey.PageDown, false, false, false);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\x1b[6~"));
    }

    [Test]
    public void KeyToVt_FunctionKeys()
    {
        Assert.That(InputHandler.KeyToVtSequence(new ConsoleKeyInfo('\0', ConsoleKey.F1, false, false, false)), Is.EqualTo("\x1bOP"));
        Assert.That(InputHandler.KeyToVtSequence(new ConsoleKeyInfo('\0', ConsoleKey.F2, false, false, false)), Is.EqualTo("\x1bOQ"));
        Assert.That(InputHandler.KeyToVtSequence(new ConsoleKeyInfo('\0', ConsoleKey.F3, false, false, false)), Is.EqualTo("\x1bOR"));
        Assert.That(InputHandler.KeyToVtSequence(new ConsoleKeyInfo('\0', ConsoleKey.F4, false, false, false)), Is.EqualTo("\x1bOS"));
        Assert.That(InputHandler.KeyToVtSequence(new ConsoleKeyInfo('\0', ConsoleKey.F5, false, false, false)), Is.EqualTo("\x1b[15~"));
        Assert.That(InputHandler.KeyToVtSequence(new ConsoleKeyInfo('\0', ConsoleKey.F12, false, false, false)), Is.EqualTo("\x1b[24~"));
    }

    [Test]
    public void KeyToVt_RegularCharacter()
    {
        var key = new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("a"));
    }

    [Test]
    public void KeyToVt_CtrlC()
    {
        var key = new ConsoleKeyInfo('\x03', ConsoleKey.C, false, false, true);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\x03"));
    }

    [Test]
    public void KeyToVt_CtrlD()
    {
        var key = new ConsoleKeyInfo('\x04', ConsoleKey.D, false, false, true);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\x04"));
    }

    [Test]
    public void KeyToVt_CtrlZ()
    {
        var key = new ConsoleKeyInfo('\x1a', ConsoleKey.Z, false, false, true);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\x1a"));
    }

    [Test]
    public void KeyToVt_CtrlA()
    {
        var key = new ConsoleKeyInfo('\x01', ConsoleKey.A, false, false, true);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo("\x01"));
    }

    [Test]
    public void KeyToVt_NullKeyChar_ReturnsEmpty()
    {
        // Key with no char and unmapped key
        var key = new ConsoleKeyInfo('\0', ConsoleKey.NoName, false, false, false);
        Assert.That(InputHandler.KeyToVtSequence(key), Is.EqualTo(""));
    }

    // ── Prefix sequence ("za") ──────────────────────────────────────────

    [Test]
    public void PrefixSequence_Z_SetsPending()
    {
        var handler = new InputHandler(new KeyBindings());
        var z = new ConsoleKeyInfo('z', ConsoleKey.Z, false, false, false);
        var commandLine = new Wmux.UI.CommandLine();
        var session = TestHelper.CreateTestSession();

        bool consumed = handler.HandleKey(z, session, commandLine, out _);

        Assert.That(consumed, Is.True);
        Assert.That(handler.IsPrefixPending, Is.True);
        Assert.That(handler.IsPrefixActive, Is.False);
    }

    [Test]
    public void PrefixSequence_ZA_ActivatesPrefix()
    {
        var handler = new InputHandler(new KeyBindings());
        var commandLine = new Wmux.UI.CommandLine();
        var session = TestHelper.CreateTestSession();
        var z = new ConsoleKeyInfo('z', ConsoleKey.Z, false, false, false);
        var a = new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false);

        handler.HandleKey(z, session, commandLine, out _);
        bool consumed = handler.HandleKey(a, session, commandLine, out _);

        Assert.That(consumed, Is.True);
        Assert.That(handler.IsPrefixActive, Is.True);
        Assert.That(handler.IsPrefixPending, Is.False);
    }

    [Test]
    public void PrefixSequence_ZX_FlushesZ()
    {
        var handler = new InputHandler(new KeyBindings());
        var commandLine = new Wmux.UI.CommandLine();
        var session = TestHelper.CreateTestSession();
        var z = new ConsoleKeyInfo('z', ConsoleKey.Z, false, false, false);
        var x = new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false);

        handler.HandleKey(z, session, commandLine, out _);
        bool consumed = handler.HandleKey(x, session, commandLine, out _);

        // 'x' is a normal character, should NOT be consumed
        Assert.That(consumed, Is.False);
        Assert.That(handler.IsPrefixPending, Is.False);
        Assert.That(handler.IsPrefixActive, Is.False);
        // Deferred 'z' should be available for forwarding
        Assert.That(handler.DeferredKeys, Has.Count.EqualTo(1));
        Assert.That(handler.DeferredKeys[0].KeyChar, Is.EqualTo('z'));
    }

    [Test]
    public void PrefixSequence_ZZ_FlushesFirstZ()
    {
        var handler = new InputHandler(new KeyBindings());
        var commandLine = new Wmux.UI.CommandLine();
        var session = TestHelper.CreateTestSession();
        var z = new ConsoleKeyInfo('z', ConsoleKey.Z, false, false, false);

        handler.HandleKey(z, session, commandLine, out _);
        bool consumed = handler.HandleKey(z, session, commandLine, out _);

        // Second 'z' is consumed (becomes new pending)
        Assert.That(consumed, Is.True);
        Assert.That(handler.IsPrefixPending, Is.True);
        // First 'z' was flushed to DeferredKeys
        Assert.That(handler.DeferredKeys, Has.Count.EqualTo(1));
        Assert.That(handler.DeferredKeys[0].KeyChar, Is.EqualTo('z'));
    }

    // ── Selection mode ──────────────────────────────────────────

    [Test]
    public void SelectionMode_ZaBracket_EntersSelectionMode()
    {
        var handler = new InputHandler(new KeyBindings());
        var commandLine = new Wmux.UI.CommandLine();
        var session = TestHelper.CreateTestSession();

        handler.HandleKey(MakeKey('z', ConsoleKey.Z), session, commandLine, out _);
        handler.HandleKey(MakeKey('a', ConsoleKey.A), session, commandLine, out _);
        handler.HandleKey(MakeKey('[', ConsoleKey.Oem4), session, commandLine, out _);

        Assert.That(handler.IsSelectionMode, Is.True);
    }

    [Test]
    public void SelectionMode_Escape_ExitsSelectionMode()
    {
        var handler = new InputHandler(new KeyBindings());
        var commandLine = new Wmux.UI.CommandLine();
        var session = TestHelper.CreateTestSession();

        // Enter selection mode
        handler.HandleKey(MakeKey('z', ConsoleKey.Z), session, commandLine, out _);
        handler.HandleKey(MakeKey('a', ConsoleKey.A), session, commandLine, out _);
        handler.HandleKey(MakeKey('[', ConsoleKey.Oem4), session, commandLine, out _);
        Assert.That(handler.IsSelectionMode, Is.True);

        // Press Escape
        handler.HandleKey(MakeKey('\x1b', ConsoleKey.Escape), session, commandLine, out _);
        Assert.That(handler.IsSelectionMode, Is.False);
    }

    [Test]
    public void SelectionMode_Q_ExitsSelectionMode()
    {
        var handler = new InputHandler(new KeyBindings());
        var commandLine = new Wmux.UI.CommandLine();
        var session = TestHelper.CreateTestSession();

        handler.HandleKey(MakeKey('z', ConsoleKey.Z), session, commandLine, out _);
        handler.HandleKey(MakeKey('a', ConsoleKey.A), session, commandLine, out _);
        handler.HandleKey(MakeKey('[', ConsoleKey.Oem4), session, commandLine, out _);
        Assert.That(handler.IsSelectionMode, Is.True);

        handler.HandleKey(MakeKey('q', ConsoleKey.Q), session, commandLine, out _);
        Assert.That(handler.IsSelectionMode, Is.False);
    }

    [Test]
    public void SelectionMode_ArrowKeys_MoveCursor()
    {
        var handler = new InputHandler(new KeyBindings());
        var commandLine = new Wmux.UI.CommandLine();
        var session = TestHelper.CreateTestSession();
        var pane = session.ActiveWindow.ActivePane;

        // Enter selection mode via za[
        handler.HandleKey(MakeKey('z', ConsoleKey.Z), session, commandLine, out _);
        handler.HandleKey(MakeKey('a', ConsoleKey.A), session, commandLine, out _);
        handler.HandleKey(MakeKey('[', ConsoleKey.Oem4), session, commandLine, out _);

        // Set known starting position
        pane.SelectionCursorRow = 5;
        pane.SelectionCursorCol = 10;

        // Move down
        handler.HandleKey(MakeKey('\0', ConsoleKey.DownArrow), session, commandLine, out _);
        Assert.That(pane.SelectionCursorRow, Is.EqualTo(6));
        Assert.That(pane.SelectionCursorCol, Is.EqualTo(10));

        // Move right
        handler.HandleKey(MakeKey('\0', ConsoleKey.RightArrow), session, commandLine, out _);
        Assert.That(pane.SelectionCursorCol, Is.EqualTo(11));

        // Move up
        handler.HandleKey(MakeKey('\0', ConsoleKey.UpArrow), session, commandLine, out _);
        Assert.That(pane.SelectionCursorRow, Is.EqualTo(5));

        // Move left
        handler.HandleKey(MakeKey('\0', ConsoleKey.LeftArrow), session, commandLine, out _);
        Assert.That(pane.SelectionCursorCol, Is.EqualTo(10));
    }

    [Test]
    public void SelectionMode_Hjkl_MoveCursor()
    {
        var handler = new InputHandler(new KeyBindings());
        var commandLine = new Wmux.UI.CommandLine();
        var session = TestHelper.CreateTestSession();
        var pane = session.ActiveWindow.ActivePane;

        // Enter selection mode via za[
        handler.HandleKey(MakeKey('z', ConsoleKey.Z), session, commandLine, out _);
        handler.HandleKey(MakeKey('a', ConsoleKey.A), session, commandLine, out _);
        handler.HandleKey(MakeKey('[', ConsoleKey.Oem4), session, commandLine, out _);

        // Set known starting position
        pane.SelectionCursorRow = 5;
        pane.SelectionCursorCol = 10;

        handler.HandleKey(MakeKey('j', ConsoleKey.J), session, commandLine, out _);
        Assert.That(pane.SelectionCursorRow, Is.EqualTo(6));

        handler.HandleKey(MakeKey('l', ConsoleKey.L), session, commandLine, out _);
        Assert.That(pane.SelectionCursorCol, Is.EqualTo(11));

        handler.HandleKey(MakeKey('k', ConsoleKey.K), session, commandLine, out _);
        Assert.That(pane.SelectionCursorRow, Is.EqualTo(5));

        handler.HandleKey(MakeKey('h', ConsoleKey.H), session, commandLine, out _);
        Assert.That(pane.SelectionCursorCol, Is.EqualTo(10));
    }

    [Test]
    public void SelectionMode_ClampsAtBoundaries()
    {
        var handler = new InputHandler(new KeyBindings());
        var commandLine = new Wmux.UI.CommandLine();
        var session = TestHelper.CreateTestSession();
        var pane = session.ActiveWindow.ActivePane;

        // Enter selection mode via za[
        handler.HandleKey(MakeKey('z', ConsoleKey.Z), session, commandLine, out _);
        handler.HandleKey(MakeKey('a', ConsoleKey.A), session, commandLine, out _);
        handler.HandleKey(MakeKey('[', ConsoleKey.Oem4), session, commandLine, out _);

        // Set to top-left corner
        pane.SelectionCursorRow = 0;
        pane.SelectionCursorCol = 0;

        // Try to move above row 0 (no scrollback, so stays at 0)
        handler.HandleKey(MakeKey('\0', ConsoleKey.UpArrow), session, commandLine, out _);
        Assert.That(pane.SelectionCursorRow, Is.EqualTo(0));

        // Try to move left of col 0
        handler.HandleKey(MakeKey('\0', ConsoleKey.LeftArrow), session, commandLine, out _);
        Assert.That(pane.SelectionCursorCol, Is.EqualTo(0));

        // Move to bottom-right corner
        pane.SelectionCursorRow = pane.Height - 1;
        pane.SelectionCursorCol = pane.Width - 1;

        // Try to move below last row
        handler.HandleKey(MakeKey('\0', ConsoleKey.DownArrow), session, commandLine, out _);
        Assert.That(pane.SelectionCursorRow, Is.EqualTo(pane.Height - 1));

        // Try to move right of last col
        handler.HandleKey(MakeKey('\0', ConsoleKey.RightArrow), session, commandLine, out _);
        Assert.That(pane.SelectionCursorCol, Is.EqualTo(pane.Width - 1));
    }

    [Test]
    public void SelectionMode_ConsumesAllKeys()
    {
        var handler = new InputHandler(new KeyBindings());
        var commandLine = new Wmux.UI.CommandLine();
        var session = TestHelper.CreateTestSession();

        // Enter selection mode via za[
        handler.HandleKey(MakeKey('z', ConsoleKey.Z), session, commandLine, out _);
        handler.HandleKey(MakeKey('a', ConsoleKey.A), session, commandLine, out _);
        handler.HandleKey(MakeKey('[', ConsoleKey.Oem4), session, commandLine, out _);

        // Random letter should be consumed
        bool consumed = handler.HandleKey(MakeKey('x', ConsoleKey.X), session, commandLine, out string? command);
        Assert.That(consumed, Is.True);
        Assert.That(command, Is.Null);
        Assert.That(handler.IsSelectionMode, Is.True);
    }

    [Test]
    public void SelectionMode_ServerMode_ZaBracket_SendsSelectionEnterCommand()
    {
        var handler = new InputHandler(new KeyBindings());
        var commandLine = new Wmux.UI.CommandLine();

        handler.HandleKeyServerMode(MakeKey('z', ConsoleKey.Z), commandLine, out _);
        handler.HandleKeyServerMode(MakeKey('a', ConsoleKey.A), commandLine, out _);
        bool consumed = handler.HandleKeyServerMode(MakeKey('[', ConsoleKey.Oem4), commandLine, out string? action);

        Assert.That(consumed, Is.True);
        Assert.That(action, Is.EqualTo("selection-enter"));
        Assert.That(handler.IsSelectionMode, Is.True);
    }

    [Test]
    public void SelectionMode_DoesNotForwardInput()
    {
        var handler = new InputHandler(new KeyBindings());
        var commandLine = new Wmux.UI.CommandLine();
        var session = TestHelper.CreateTestSession();

        // Enter selection mode via za[
        handler.HandleKey(MakeKey('z', ConsoleKey.Z), session, commandLine, out _);
        handler.HandleKey(MakeKey('a', ConsoleKey.A), session, commandLine, out _);
        handler.HandleKey(MakeKey('[', ConsoleKey.Oem4), session, commandLine, out _);

        // Normal characters should be consumed (not forwarded to pane)
        bool consumed1 = handler.HandleKey(MakeKey('a', ConsoleKey.A), session, commandLine, out _);
        bool consumed2 = handler.HandleKey(MakeKey('b', ConsoleKey.B), session, commandLine, out _);

        Assert.That(consumed1, Is.True);
        Assert.That(consumed2, Is.True);
    }

    // ── Selection highlight ─────────────────────────────────────

    [Test]
    public void SelectionMode_Space_StartsHighlight()
    {
        var handler = new InputHandler(new KeyBindings());
        var commandLine = new Wmux.UI.CommandLine();
        var session = TestHelper.CreateTestSession();
        var pane = session.ActiveWindow.ActivePane;

        // Enter selection mode via za[
        handler.HandleKey(MakeKey('z', ConsoleKey.Z), session, commandLine, out _);
        handler.HandleKey(MakeKey('a', ConsoleKey.A), session, commandLine, out _);
        handler.HandleKey(MakeKey('[', ConsoleKey.Oem4), session, commandLine, out _);
        Assert.That(pane.SelectionHighlightActive, Is.False);

        // Press SPACE to start highlighting
        handler.HandleKey(MakeKey(' ', ConsoleKey.Spacebar), session, commandLine, out _);
        Assert.That(pane.SelectionHighlightActive, Is.True);
        Assert.That(handler.IsSelectionMode, Is.True);
    }

    [Test]
    public void SelectionMode_SecondSpace_CopiesAndExits()
    {
        var handler = new InputHandler(new KeyBindings());
        var commandLine = new Wmux.UI.CommandLine();
        var session = TestHelper.CreateTestSession();
        var pane = session.ActiveWindow.ActivePane;

        // Enter selection mode and start highlight
        handler.HandleKey(MakeKey('z', ConsoleKey.Z), session, commandLine, out _);
        handler.HandleKey(MakeKey('a', ConsoleKey.A), session, commandLine, out _);
        handler.HandleKey(MakeKey('[', ConsoleKey.Oem4), session, commandLine, out _);
        handler.HandleKey(MakeKey(' ', ConsoleKey.Spacebar), session, commandLine, out _);
        Assert.That(pane.SelectionHighlightActive, Is.True);

        // Press SPACE again to copy and exit
        handler.HandleKey(MakeKey(' ', ConsoleKey.Spacebar), session, commandLine, out _);
        Assert.That(handler.IsSelectionMode, Is.False);
        Assert.That(pane.IsInSelectionMode, Is.False);
        Assert.That(pane.SelectionHighlightActive, Is.False);
    }

    [Test]
    public void SelectionMode_EscDuringHighlight_ExitsCleanly()
    {
        var handler = new InputHandler(new KeyBindings());
        var commandLine = new Wmux.UI.CommandLine();
        var session = TestHelper.CreateTestSession();
        var pane = session.ActiveWindow.ActivePane;

        // Enter selection mode and start highlight
        handler.HandleKey(MakeKey('z', ConsoleKey.Z), session, commandLine, out _);
        handler.HandleKey(MakeKey('a', ConsoleKey.A), session, commandLine, out _);
        handler.HandleKey(MakeKey('[', ConsoleKey.Oem4), session, commandLine, out _);
        handler.HandleKey(MakeKey(' ', ConsoleKey.Spacebar), session, commandLine, out _);
        Assert.That(pane.SelectionHighlightActive, Is.True);

        // Press Esc to exit without copying
        handler.HandleKey(MakeKey('\x1b', ConsoleKey.Escape), session, commandLine, out _);
        Assert.That(handler.IsSelectionMode, Is.False);
        Assert.That(pane.IsInSelectionMode, Is.False);
        Assert.That(pane.SelectionHighlightActive, Is.False);
    }

    [Test]
    public void SelectionMode_ServerMode_Space_SendsSelectionToggle()
    {
        var handler = new InputHandler(new KeyBindings());
        var commandLine = new Wmux.UI.CommandLine();

        // Enter selection mode
        handler.HandleKeyServerMode(MakeKey('z', ConsoleKey.Z), commandLine, out _);
        handler.HandleKeyServerMode(MakeKey('a', ConsoleKey.A), commandLine, out _);
        handler.HandleKeyServerMode(MakeKey('[', ConsoleKey.Oem4), commandLine, out _);

        // Press SPACE
        bool consumed = handler.HandleKeyServerMode(MakeKey(' ', ConsoleKey.Spacebar), commandLine, out string? action);
        Assert.That(consumed, Is.True);
        Assert.That(action, Is.EqualTo("selection-toggle"));
    }

    private static ConsoleKeyInfo MakeKey(char c, ConsoleKey key) =>
        new ConsoleKeyInfo(c, key, false, false, false);

    /// <summary>
    /// Helper to create a minimal Session for testing HandleKey.
    /// </summary>
    private static class TestHelper
    {
        public static Wmux.Core.Session CreateTestSession()
        {
            return new Wmux.Core.Session("test", 80, 24);
        }
    }
}
