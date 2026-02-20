using Wmux.Config;

namespace Wmux.Tests;

[TestFixture]
public class KeyBindingsTests
{
    private KeyBindings _keys = null!;

    [SetUp]
    public void SetUp()
    {
        _keys = new KeyBindings();
    }

    [Test]
    public void Defaults_PrefixIsCtrlA()
    {
        Assert.That(_keys.PrefixModifier, Is.EqualTo(ConsoleModifiers.Control));
        Assert.That(_keys.PrefixKey, Is.EqualTo(ConsoleKey.A));
    }

    [Test]
    public void Defaults_SplitKeys()
    {
        Assert.That(_keys.SplitHorizontal, Is.EqualTo('s'));
        Assert.That(_keys.SplitVertical, Is.EqualTo('|'));
    }

    [Test]
    public void Defaults_WindowKeys()
    {
        Assert.That(_keys.NewWindow, Is.EqualTo('c'));
        Assert.That(_keys.NextWindow, Is.EqualTo('n'));
        Assert.That(_keys.PrevWindow, Is.EqualTo('p'));
    }

    [Test]
    public void Defaults_PaneKeys()
    {
        Assert.That(_keys.KillPane, Is.EqualTo('x'));
        Assert.That(_keys.NextPane, Is.EqualTo('o'));
    }

    [Test]
    public void Defaults_OtherKeys()
    {
        Assert.That(_keys.Detach, Is.EqualTo('d'));
        Assert.That(_keys.CommandMode, Is.EqualTo(':'));
        Assert.That(_keys.CopyMode, Is.EqualTo('['));
        Assert.That(_keys.RenameWindow, Is.EqualTo(','));
        Assert.That(_keys.KillWindow, Is.EqualTo('&'));
        Assert.That(_keys.CycleLayout, Is.EqualTo(' '));
    }

    [Test]
    public void IsPrefixKey_MatchesCtrlA()
    {
        var key = new ConsoleKeyInfo('\x01', ConsoleKey.A, false, false, true);
        Assert.That(_keys.IsPrefixKey(key), Is.True);
    }

    [Test]
    public void IsPrefixKey_DoesNotMatchPlainA()
    {
        var key = new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false);
        Assert.That(_keys.IsPrefixKey(key), Is.False);
    }

    [Test]
    public void IsPrefixKey_DoesNotMatchCtrlB()
    {
        var key = new ConsoleKeyInfo('\x02', ConsoleKey.B, false, false, true);
        Assert.That(_keys.IsPrefixKey(key), Is.False);
    }

    [Test]
    public void IsPrefixKey_CustomPrefixKey()
    {
        _keys.PrefixKey = ConsoleKey.A;
        var key = new ConsoleKeyInfo('\x01', ConsoleKey.A, false, false, true);
        Assert.That(_keys.IsPrefixKey(key), Is.True);
    }

    [Test]
    public void IsPrefixKey_DoesNotMatchWithShift()
    {
        // Ctrl+Shift+A should not match plain Ctrl+A
        var key = new ConsoleKeyInfo('\x01', ConsoleKey.A, true, false, true);
        Assert.That(_keys.IsPrefixKey(key), Is.False);
    }

    [Test]
    public void IsPrefixKey_DoesNotMatchWithAlt()
    {
        var key = new ConsoleKeyInfo('\x01', ConsoleKey.A, false, true, true);
        Assert.That(_keys.IsPrefixKey(key), Is.False);
    }
}
