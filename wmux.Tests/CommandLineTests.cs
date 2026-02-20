using Wmux.UI;

namespace Wmux.Tests;

[TestFixture]
public class CommandLineTests
{
    private CommandLine _cmd = null!;

    [SetUp]
    public void SetUp()
    {
        _cmd = new CommandLine();
    }

    // ── Initial state ────────────────────────────────────────────────

    [Test]
    public void InitialState_IsNotActive()
    {
        Assert.That(_cmd.IsActive, Is.False);
    }

    [Test]
    public void InitialState_InputIsEmpty()
    {
        Assert.That(_cmd.Input, Is.EqualTo(""));
    }

    // ── Activate / Deactivate ────────────────────────────────────────

    [Test]
    public void Activate_SetsActive()
    {
        _cmd.Activate();
        Assert.That(_cmd.IsActive, Is.True);
    }

    [Test]
    public void Activate_ClearsInput()
    {
        _cmd.Activate();
        _cmd.HandleKey(MakeKey('a'));
        _cmd.Deactivate();
        _cmd.Activate();
        Assert.That(_cmd.Input, Is.EqualTo(""));
    }

    [Test]
    public void Deactivate_ClearsActive()
    {
        _cmd.Activate();
        _cmd.Deactivate();
        Assert.That(_cmd.IsActive, Is.False);
    }

    [Test]
    public void Deactivate_ClearsInput()
    {
        _cmd.Activate();
        _cmd.HandleKey(MakeKey('a'));
        _cmd.Deactivate();
        Assert.That(_cmd.Input, Is.EqualTo(""));
    }

    // ── HandleKey when inactive ──────────────────────────────────────

    [Test]
    public void HandleKey_WhenInactive_ReturnsNull()
    {
        var result = _cmd.HandleKey(MakeKey('a'));
        Assert.That(result, Is.Null);
    }

    // ── HandleKey - character input ──────────────────────────────────

    [Test]
    public void HandleKey_PrintableChar_AppendsToInput()
    {
        _cmd.Activate();
        _cmd.HandleKey(MakeKey('k'));
        _cmd.HandleKey(MakeKey('i'));
        _cmd.HandleKey(MakeKey('l'));
        _cmd.HandleKey(MakeKey('l'));
        Assert.That(_cmd.Input, Is.EqualTo("kill"));
    }

    [Test]
    public void HandleKey_PrintableChar_ReturnsNull()
    {
        _cmd.Activate();
        var result = _cmd.HandleKey(MakeKey('a'));
        Assert.That(result, Is.Null);
    }

    // ── HandleKey - Enter ────────────────────────────────────────────

    [Test]
    public void HandleKey_Enter_ReturnsInput()
    {
        _cmd.Activate();
        _cmd.HandleKey(MakeKey('h'));
        _cmd.HandleKey(MakeKey('i'));
        var result = _cmd.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        Assert.That(result, Is.EqualTo("hi"));
    }

    [Test]
    public void HandleKey_Enter_DeactivatesCommandLine()
    {
        _cmd.Activate();
        _cmd.HandleKey(MakeKey('x'));
        _cmd.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        Assert.That(_cmd.IsActive, Is.False);
    }

    [Test]
    public void HandleKey_Enter_EmptyInput_ReturnsEmptyString()
    {
        _cmd.Activate();
        var result = _cmd.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        Assert.That(result, Is.EqualTo(""));
    }

    // ── HandleKey - Escape ───────────────────────────────────────────

    [Test]
    public void HandleKey_Escape_Deactivates()
    {
        _cmd.Activate();
        _cmd.HandleKey(MakeKey('a'));
        _cmd.HandleKey(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));
        Assert.That(_cmd.IsActive, Is.False);
    }

    [Test]
    public void HandleKey_Escape_ReturnsNull()
    {
        _cmd.Activate();
        var result = _cmd.HandleKey(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));
        Assert.That(result, Is.Null);
    }

    // ── HandleKey - Backspace ────────────────────────────────────────

    [Test]
    public void HandleKey_Backspace_RemovesLastChar()
    {
        _cmd.Activate();
        _cmd.HandleKey(MakeKey('a'));
        _cmd.HandleKey(MakeKey('b'));
        _cmd.HandleKey(MakeKey('c'));
        _cmd.HandleKey(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
        Assert.That(_cmd.Input, Is.EqualTo("ab"));
    }

    [Test]
    public void HandleKey_Backspace_OnEmpty_StaysEmpty()
    {
        _cmd.Activate();
        _cmd.HandleKey(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
        Assert.That(_cmd.Input, Is.EqualTo(""));
    }

    [Test]
    public void HandleKey_Backspace_ReturnsNull()
    {
        _cmd.Activate();
        _cmd.HandleKey(MakeKey('a'));
        var result = _cmd.HandleKey(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
        Assert.That(result, Is.Null);
    }

    // ── Spaces and special characters ────────────────────────────────

    [Test]
    public void HandleKey_SpaceCharacter()
    {
        _cmd.Activate();
        _cmd.HandleKey(MakeKey('a'));
        _cmd.HandleKey(MakeKey(' '));
        _cmd.HandleKey(MakeKey('b'));
        Assert.That(_cmd.Input, Is.EqualTo("a b"));
    }

    [Test]
    public void HandleKey_HyphenCharacter()
    {
        _cmd.Activate();
        _cmd.HandleKey(MakeKey('-'));
        _cmd.HandleKey(MakeKey('v'));
        Assert.That(_cmd.Input, Is.EqualTo("-v"));
    }

    private static ConsoleKeyInfo MakeKey(char c)
    {
        var key = c switch
        {
            ' ' => ConsoleKey.Spacebar,
            '-' => ConsoleKey.OemMinus,
            _ when c >= 'a' && c <= 'z' => ConsoleKey.A + (c - 'a'),
            _ when c >= 'A' && c <= 'Z' => ConsoleKey.A + (c - 'A'),
            _ when c >= '0' && c <= '9' => ConsoleKey.D0 + (c - '0'),
            _ => ConsoleKey.NoName
        };
        return new ConsoleKeyInfo(c, key, false, false, false);
    }
}
