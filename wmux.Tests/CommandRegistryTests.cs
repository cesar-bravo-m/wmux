using Wmux.Commands;
using Wmux.Core;

namespace Wmux.Tests;

/// <summary>
/// Tests for CommandRegistry. Spawns real processes (ConPTY).
/// </summary>
[TestFixture]
[Platform("Win")]
public class CommandRegistryTests
{
    private CommandRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = new CommandRegistry();
    }

    // ── split-window ─────────────────────────────────────────────────

    [Test]
    public void SplitWindow_Horizontal_SplitsActivePane()
    {
        using var session = new Session("test", 80, 24);
        var cmd = new ParsedCommand("split-window", ["-h"]);
        var result = _registry.Execute(cmd, session);

        Assert.That(result, Is.Null); // null means success
        Assert.That(session.ActiveWindow.GetPanes(), Has.Count.EqualTo(2));
    }

    [Test]
    public void SplitWindow_Vertical_SplitsActivePane()
    {
        using var session = new Session("test", 80, 24);
        var cmd = new ParsedCommand("split-window", ["-v"]);
        var result = _registry.Execute(cmd, session);

        Assert.That(result, Is.Null);
        Assert.That(session.ActiveWindow.GetPanes(), Has.Count.EqualTo(2));
    }

    [Test]
    public void SplitWindow_Alias()
    {
        using var session = new Session("test", 80, 24);
        var cmd = new ParsedCommand("splitw", ["-v"]);
        var result = _registry.Execute(cmd, session);

        Assert.That(result, Is.Null);
        Assert.That(session.ActiveWindow.GetPanes(), Has.Count.EqualTo(2));
    }

    // ── new-window ───────────────────────────────────────────────────

    [Test]
    public void NewWindow_CreatesNewWindow()
    {
        using var session = new Session("test", 80, 24);
        var cmd = new ParsedCommand("new-window", []);
        _registry.Execute(cmd, session);

        Assert.That(session.Windows, Has.Count.EqualTo(2));
    }

    [Test]
    public void NewWindow_WithName()
    {
        using var session = new Session("test", 80, 24);
        var cmd = new ParsedCommand("new-window", ["mywin"]);
        _registry.Execute(cmd, session);

        Assert.That(session.ActiveWindow.Name, Is.EqualTo("mywin"));
    }

    [Test]
    public void NewWindow_Alias()
    {
        using var session = new Session("test", 80, 24);
        var cmd = new ParsedCommand("neww", []);
        _registry.Execute(cmd, session);

        Assert.That(session.Windows, Has.Count.EqualTo(2));
    }

    // ── kill-pane ────────────────────────────────────────────────────

    [Test]
    public void KillPane_RemovesActivePane()
    {
        using var session = new Session("test", 80, 24);
        session.ActiveWindow.SplitPane(SplitDirection.Horizontal);
        Assert.That(session.ActiveWindow.GetPanes(), Has.Count.EqualTo(2));

        var cmd = new ParsedCommand("kill-pane", []);
        _registry.Execute(cmd, session);

        // KillPane should close the active pane when more than 1 exists
        Assert.That(session.ActiveWindow.GetPanes(), Has.Count.EqualTo(1));
    }

    [Test]
    public void KillPane_SinglePane_SignalsDestroySession()
    {
        using var session = new Session("test", 80, 24);
        var cmd = new ParsedCommand("kill-pane", []);
        var result = _registry.Execute(cmd, session);

        // Last pane in last window — returns destroy-session signal for server
        Assert.That(result, Is.EqualTo("\x01destroy-session"));
        // Session is not modified; the server handles destruction
        Assert.That(session.ActiveWindow.GetPanes(), Has.Count.EqualTo(1));
    }

    // ── kill-window ──────────────────────────────────────────────────

    [Test]
    public void KillWindow_RemovesActiveWindow()
    {
        using var session = new Session("test", 80, 24);
        session.CreateWindow(80, 24);
        Assert.That(session.Windows, Has.Count.EqualTo(2));

        var cmd = new ParsedCommand("kill-window", []);
        _registry.Execute(cmd, session);

        Assert.That(session.Windows, Has.Count.EqualTo(1));
    }

    [Test]
    public void KillWindow_LastWindow_SignalsDestroySession()
    {
        using var session = new Session("test", 80, 24);
        var cmd = new ParsedCommand("kill-window", []);
        var result = _registry.Execute(cmd, session);

        // Last window — returns destroy-session signal for server
        Assert.That(result, Is.EqualTo("\x01destroy-session"));
        Assert.That(session.Windows, Has.Count.EqualTo(1));
    }

    // ── select-window ────────────────────────────────────────────────

    [Test]
    public void SelectWindow_ByIndex()
    {
        using var session = new Session("test", 80, 24);
        session.CreateWindow(80, 24);
        session.CreateWindow(80, 24);

        var cmd = new ParsedCommand("select-window", ["0"]);
        _registry.Execute(cmd, session);

        Assert.That(session.ActiveWindow, Is.EqualTo(session.Windows[0]));
    }

    // ── rename-window ────────────────────────────────────────────────

    [Test]
    public void RenameWindow_ChangesName()
    {
        using var session = new Session("test", 80, 24);
        var cmd = new ParsedCommand("rename-window", ["newname"]);
        _registry.Execute(cmd, session);

        Assert.That(session.ActiveWindow.Name, Is.EqualTo("newname"));
    }

    [Test]
    public void RenameWindow_MultipleWords()
    {
        using var session = new Session("test", 80, 24);
        var cmd = new ParsedCommand("rename-window", ["my", "window"]);
        _registry.Execute(cmd, session);

        Assert.That(session.ActiveWindow.Name, Is.EqualTo("my window"));
    }

    // ── list-windows ─────────────────────────────────────────────────

    [Test]
    public void ListWindows_ReturnsOutput()
    {
        using var session = new Session("test", 80, 24);
        session.CreateWindow(80, 24);

        var cmd = new ParsedCommand("list-windows", []);
        var result = _registry.Execute(cmd, session);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("0:"));
        Assert.That(result, Does.Contain("1:"));
        Assert.That(result, Does.Contain("(active)"));
    }

    // ── list-panes ───────────────────────────────────────────────────

    [Test]
    public void ListPanes_ReturnsOutput()
    {
        using var session = new Session("test", 80, 24);
        session.ActiveWindow.SplitPane(SplitDirection.Horizontal);

        var cmd = new ParsedCommand("list-panes", []);
        var result = _registry.Execute(cmd, session);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("(active)"));
    }

    // ── select-layout ────────────────────────────────────────────────

    [Test]
    public void SelectLayout_AppliesPreset()
    {
        using var session = new Session("test", 80, 24);
        session.ActiveWindow.SplitPane(SplitDirection.Horizontal);

        var cmd = new ParsedCommand("select-layout", ["even-vertical"]);
        var result = _registry.Execute(cmd, session);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void SelectLayout_NoArgs_ReturnsUsage()
    {
        using var session = new Session("test", 80, 24);
        var cmd = new ParsedCommand("select-layout", []);
        var result = _registry.Execute(cmd, session);

        Assert.That(result, Does.Contain("Usage"));
    }

    // ── Unknown command ──────────────────────────────────────────────

    [Test]
    public void UnknownCommand_ReturnsError()
    {
        using var session = new Session("test", 80, 24);
        var cmd = new ParsedCommand("nonexistent", []);
        var result = _registry.Execute(cmd, session);

        Assert.That(result, Does.Contain("Unknown command"));
    }

    // ── Aliases ──────────────────────────────────────────────────────

    [Test]
    public void Aliases_AllWork()
    {
        using var session = new Session("test", 80, 24);

        // killp on last pane returns destroy-session signal
        Assert.That(_registry.Execute(new ParsedCommand("killp", []), session), Is.EqualTo("\x01destroy-session"));
        Assert.That(_registry.Execute(new ParsedCommand("lsw", []), session), Is.Not.Null); // list output
        Assert.That(_registry.Execute(new ParsedCommand("lsp", []), session), Is.Not.Null);

        session.CreateWindow(80, 24);
        Assert.That(_registry.Execute(new ParsedCommand("killw", []), session), Is.Null);

        Assert.That(_registry.Execute(new ParsedCommand("selectw", ["0"]), session), Is.Null);
        Assert.That(_registry.Execute(new ParsedCommand("renamew", ["test"]), session), Is.Null);
    }
}
