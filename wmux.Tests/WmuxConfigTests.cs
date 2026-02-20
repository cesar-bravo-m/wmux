using Wmux.Config;

namespace Wmux.Tests;

[TestFixture]
public class WmuxConfigTests
{
    [Test]
    public void Load_WhenNoConfigFile_ReturnsDefaults()
    {
        var config = WmuxConfig.Load();
        Assert.That(config, Is.Not.Null);
        Assert.That(config.Keys, Is.Not.Null);
        Assert.That(config.ScrollbackLimit, Is.EqualTo(10000));
        Assert.That(config.DefaultShell, Is.EqualTo(""));
    }

    [Test]
    public void DefaultConfig_HasValidKeyBindings()
    {
        var config = WmuxConfig.Load();
        Assert.That(config.Keys.PrefixKey, Is.EqualTo(ConsoleKey.A));
        Assert.That(config.Keys.PrefixModifier, Is.EqualTo(ConsoleModifiers.Control));
    }
}
