using Wmux.Core;
using StatusBar = Wmux.UI.StatusBar;

namespace Wmux.Tests;

/// <summary>
/// Tests for StatusBar rendering. These tests create real Session/Window/Pane objects
/// which spawn real ConPTY processes. We mark them as Platform to flag this dependency.
/// </summary>
[TestFixture]
[Platform("Win")]
public class StatusBarTests
{
    [Test]
    public void Render_ContainsSessionName()
    {
        using var session = new Session("test-session", 80, 24);
        var result = StatusBar.Render(session, 80);

        Assert.That(result, Does.Contain("[test-session]"));
    }

    [Test]
    public void Render_ContainsWindowName()
    {
        using var session = new Session("s", 80, 24);
        session.ActiveWindow.Name = "mywin";
        var result = StatusBar.Render(session, 80);

        Assert.That(result, Does.Contain("mywin"));
    }

    [Test]
    public void Render_ActiveWindowHasStar()
    {
        using var session = new Session("s", 80, 24);
        var result = StatusBar.Render(session, 80);

        Assert.That(result, Does.Contain("0:0*"));
    }

    [Test]
    public void Render_ContainsGreenBackground()
    {
        using var session = new Session("s", 80, 24);
        var result = StatusBar.Render(session, 80);

        // Green background ANSI code
        Assert.That(result, Does.Contain("\x1b[30;42m"));
    }

    [Test]
    public void Render_ContainsResetCode()
    {
        using var session = new Session("s", 80, 24);
        var result = StatusBar.Render(session, 80);

        Assert.That(result, Does.Contain("\x1b[0m"));
    }

    [Test]
    public void Render_CommandInput_ShowsColonPrompt()
    {
        using var session = new Session("s", 80, 24);
        var result = StatusBar.Render(session, 80, "kill-pane");

        Assert.That(result, Does.Contain(":kill-pane"));
    }

    [Test]
    public void Render_CommandInput_DoesNotShowSessionInfo()
    {
        using var session = new Session("mysession", 80, 24);
        var result = StatusBar.Render(session, 80, "test");

        // When in command mode, session name should not appear
        Assert.That(result, Does.Not.Contain("[mysession]"));
    }

    [Test]
    public void Render_ContainsPaneCount()
    {
        using var session = new Session("s", 80, 24);
        var result = StatusBar.Render(session, 80);

        Assert.That(result, Does.Contain("[1/1]"));
    }

    [Test]
    public void Render_ContainsTime()
    {
        using var session = new Session("s", 80, 24);
        var result = StatusBar.Render(session, 80);

        // Should contain current time in HH:mm format
        var time = DateTime.Now.ToString("HH:mm");
        Assert.That(result, Does.Contain(time));
    }

    [Test]
    public void Render_MultipleWindows_ShowsAll()
    {
        using var session = new Session("s", 80, 24);
        session.CreateWindow(80, 24);
        var result = StatusBar.Render(session, 120);

        Assert.That(result, Does.Contain("0:"));
        Assert.That(result, Does.Contain("1:"));
    }
}
