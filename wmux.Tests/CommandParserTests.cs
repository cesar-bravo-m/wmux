using Wmux.Commands;

namespace Wmux.Tests;

[TestFixture]
public class CommandParserTests
{
    [Test]
    public void Parse_SimpleCommand()
    {
        var result = CommandParser.Parse("kill-pane");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("kill-pane"));
        Assert.That(result.Args, Is.Empty);
    }

    [Test]
    public void Parse_CommandWithArgs()
    {
        var result = CommandParser.Parse("split-window -v");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("split-window"));
        Assert.That(result.Args, Has.Length.EqualTo(1));
        Assert.That(result.Args[0], Is.EqualTo("-v"));
    }

    [Test]
    public void Parse_CommandWithMultipleArgs()
    {
        var result = CommandParser.Parse("rename-window My Window");
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("rename-window"));
        Assert.That(result.Args, Has.Length.EqualTo(2));
        Assert.That(result.Args[0], Is.EqualTo("My"));
        Assert.That(result.Args[1], Is.EqualTo("Window"));
    }

    [Test]
    public void Parse_EmptyString_ReturnsNull()
    {
        Assert.That(CommandParser.Parse(""), Is.Null);
    }

    [Test]
    public void Parse_Whitespace_ReturnsNull()
    {
        Assert.That(CommandParser.Parse("   "), Is.Null);
    }

    [Test]
    public void Parse_Null_ReturnsNull()
    {
        Assert.That(CommandParser.Parse(null!), Is.Null);
    }

    [Test]
    public void Parse_ConvertsCommandNameToLowerCase()
    {
        var result = CommandParser.Parse("KILL-PANE");
        Assert.That(result!.Name, Is.EqualTo("kill-pane"));
    }

    [Test]
    public void Parse_PreservesArgCase()
    {
        var result = CommandParser.Parse("rename-window MyWindow");
        Assert.That(result!.Args[0], Is.EqualTo("MyWindow"));
    }

    [Test]
    public void Parse_TrimsLeadingWhitespace()
    {
        var result = CommandParser.Parse("   kill-pane");
        Assert.That(result!.Name, Is.EqualTo("kill-pane"));
    }

    [Test]
    public void Parse_HandlesMultipleSpacesBetweenArgs()
    {
        var result = CommandParser.Parse("split-window    -v");
        Assert.That(result!.Args, Has.Length.EqualTo(1));
        Assert.That(result.Args[0], Is.EqualTo("-v"));
    }

    [Test]
    public void Parse_SelectWindowWithNumber()
    {
        var result = CommandParser.Parse("select-window 3");
        Assert.That(result!.Name, Is.EqualTo("select-window"));
        Assert.That(result.Args[0], Is.EqualTo("3"));
    }
}
