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

    // ── HandleKey prefix logic (unit testing without Session) ─────────

    [Test]
    public void IsPrefixKey_CtrlA_Recognized()
    {
        var keys = new KeyBindings();
        var key = new ConsoleKeyInfo('\x01', ConsoleKey.A, false, false, true);
        Assert.That(keys.IsPrefixKey(key), Is.True);
    }
}
