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
    public void Defaults_ActivationStringIsZA()
    {
        Assert.That(_keys.ActivationString, Is.EqualTo("za"));
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
    public void ActivationString_CanBeChanged()
    {
        _keys.ActivationString = "qq";
        Assert.That(_keys.ActivationString, Is.EqualTo("qq"));
    }

    [Test]
    public void ActivationString_CanBeLonger()
    {
        _keys.ActivationString = "wmux";
        Assert.That(_keys.ActivationString, Is.EqualTo("wmux"));
    }
}
