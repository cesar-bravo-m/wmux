using Wmux.Core;

namespace Wmux.Tests;

/// <summary>
/// Tests for Session and Window models. These spawn real ConPTY processes.
/// </summary>
[TestFixture]
[Platform("Win")]
public class SessionTests
{
    [Test]
    public void Constructor_CreatesOneWindow()
    {
        using var session = new Session("test", 80, 24);
        Assert.That(session.Windows, Has.Count.EqualTo(1));
    }

    [Test]
    public void Constructor_SetsName()
    {
        using var session = new Session("mysession", 80, 24);
        Assert.That(session.Name, Is.EqualTo("mysession"));
    }

    [Test]
    public void Constructor_SetsActiveWindow()
    {
        using var session = new Session("test", 80, 24);
        Assert.That(session.ActiveWindow, Is.Not.Null);
        Assert.That(session.ActiveWindow, Is.EqualTo(session.Windows[0]));
    }

    [Test]
    public void Constructor_SetsCreatedAt()
    {
        var before = DateTime.Now;
        using var session = new Session("test", 80, 24);
        var after = DateTime.Now;

        Assert.That(session.CreatedAt, Is.GreaterThanOrEqualTo(before));
        Assert.That(session.CreatedAt, Is.LessThanOrEqualTo(after));
    }

    [Test]
    public void CreateWindow_AddsNewWindow()
    {
        using var session = new Session("test", 80, 24);
        session.CreateWindow(80, 24);
        Assert.That(session.Windows, Has.Count.EqualTo(2));
    }

    [Test]
    public void CreateWindow_SetsNewWindowAsActive()
    {
        using var session = new Session("test", 80, 24);
        var newWin = session.CreateWindow(80, 24);
        Assert.That(session.ActiveWindow, Is.EqualTo(newWin));
    }

    [Test]
    public void CloseWindow_RemovesWindow()
    {
        using var session = new Session("test", 80, 24);
        var win2 = session.CreateWindow(80, 24);
        session.CloseWindow(win2);
        Assert.That(session.Windows, Has.Count.EqualTo(1));
    }

    [Test]
    public void CloseWindow_LastWindow_DoesNotRemove()
    {
        using var session = new Session("test", 80, 24);
        session.CloseWindow(session.ActiveWindow);
        Assert.That(session.Windows, Has.Count.EqualTo(1));
    }

    [Test]
    public void CloseWindow_ActiveWindow_SwitchesToAnother()
    {
        using var session = new Session("test", 80, 24);
        var win1 = session.ActiveWindow;
        var win2 = session.CreateWindow(80, 24);
        session.ActiveWindow = win2;
        session.CloseWindow(win2);
        Assert.That(session.ActiveWindow, Is.EqualTo(win1));
    }

    [Test]
    public void NextWindow_CyclesToNextWindow()
    {
        using var session = new Session("test", 80, 24);
        var win1 = session.ActiveWindow;
        session.CreateWindow(80, 24);
        session.ActiveWindow = win1; // Go back to first
        session.NextWindow();
        Assert.That(session.ActiveWindow, Is.EqualTo(session.Windows[1]));
    }

    [Test]
    public void NextWindow_CyclesAroundToFirst()
    {
        using var session = new Session("test", 80, 24);
        session.CreateWindow(80, 24);
        // Now on window 1 (the second)
        session.NextWindow();
        Assert.That(session.ActiveWindow, Is.EqualTo(session.Windows[0]));
    }

    [Test]
    public void PrevWindow_CyclesToPreviousWindow()
    {
        using var session = new Session("test", 80, 24);
        session.CreateWindow(80, 24);
        // On window 1
        session.PrevWindow();
        Assert.That(session.ActiveWindow, Is.EqualTo(session.Windows[0]));
    }

    [Test]
    public void PrevWindow_CyclesAroundToLast()
    {
        using var session = new Session("test", 80, 24);
        session.CreateWindow(80, 24);
        session.ActiveWindow = session.Windows[0]; // First window
        session.PrevWindow(); // Should wrap to last
        Assert.That(session.ActiveWindow, Is.EqualTo(session.Windows[1]));
    }

    [Test]
    public void SelectWindow_ByIndex()
    {
        using var session = new Session("test", 80, 24);
        session.CreateWindow(80, 24);
        session.CreateWindow(80, 24);
        session.SelectWindow(0);
        Assert.That(session.ActiveWindow, Is.EqualTo(session.Windows[0]));
        session.SelectWindow(2);
        Assert.That(session.ActiveWindow, Is.EqualTo(session.Windows[2]));
    }

    [Test]
    public void SelectWindow_InvalidIndex_DoesNothing()
    {
        using var session = new Session("test", 80, 24);
        var active = session.ActiveWindow;
        session.SelectWindow(99);
        Assert.That(session.ActiveWindow, Is.EqualTo(active));
    }

    [Test]
    public void SelectWindow_NegativeIndex_DoesNothing()
    {
        using var session = new Session("test", 80, 24);
        var active = session.ActiveWindow;
        session.SelectWindow(-1);
        Assert.That(session.ActiveWindow, Is.EqualTo(active));
    }

    [Test]
    public void Resize_ResizesAllWindows()
    {
        using var session = new Session("test", 80, 24);
        session.CreateWindow(80, 24);
        session.Resize(120, 40);

        foreach (var win in session.Windows)
        {
            Assert.That(win.Width, Is.EqualTo(120));
            Assert.That(win.Height, Is.EqualTo(40));
        }
    }
}

[TestFixture]
[Platform("Win")]
public class WindowTests
{
    [Test]
    public void Constructor_CreatesOnePane()
    {
        using var win = new Window("test", 80, 24);
        Assert.That(win.GetPanes(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Constructor_SetsNameAndDimensions()
    {
        using var win = new Window("mywin", 120, 40);
        Assert.That(win.Name, Is.EqualTo("mywin"));
        Assert.That(win.Width, Is.EqualTo(120));
        Assert.That(win.Height, Is.EqualTo(40));
    }

    [Test]
    public void Constructor_SetsActivePaneToFirst()
    {
        using var win = new Window("test", 80, 24);
        Assert.That(win.ActivePane, Is.EqualTo(win.GetPanes()[0]));
    }

    [Test]
    public void SplitPane_Horizontal_CreatesTwoPanes()
    {
        using var win = new Window("test", 80, 24);
        var newPane = win.SplitPane(SplitDirection.Horizontal);
        Assert.That(newPane, Is.Not.Null);
        Assert.That(win.GetPanes(), Has.Count.EqualTo(2));
    }

    [Test]
    public void SplitPane_Vertical_CreatesTwoPanes()
    {
        using var win = new Window("test", 80, 24);
        var newPane = win.SplitPane(SplitDirection.Vertical);
        Assert.That(newPane, Is.Not.Null);
        Assert.That(win.GetPanes(), Has.Count.EqualTo(2));
    }

    [Test]
    public void SplitPane_TooSmall_ReturnsNull()
    {
        using var win = new Window("test", 6, 3);
        // Try to split a tiny pane
        var result = win.SplitPane(SplitDirection.Horizontal);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ClosePane_RemovesPane()
    {
        using var win = new Window("test", 80, 24);
        win.SplitPane(SplitDirection.Horizontal);
        Assert.That(win.GetPanes(), Has.Count.EqualTo(2));

        var paneToClose = win.GetPanes()[1];
        win.ClosePane(paneToClose);
        Assert.That(win.GetPanes(), Has.Count.EqualTo(1));
    }

    [Test]
    public void ClosePane_LastPane_DoesNotRemove()
    {
        using var win = new Window("test", 80, 24);
        win.ClosePane(win.ActivePane);
        Assert.That(win.GetPanes(), Has.Count.EqualTo(1));
    }

    [Test]
    public void ClosePane_ActivePane_SwitchesToAnother()
    {
        using var win = new Window("test", 80, 24);
        var newPane = win.SplitPane(SplitDirection.Horizontal);
        win.ActivePane = newPane!;
        win.ClosePane(newPane!);
        Assert.That(win.ActivePane, Is.Not.Null);
        Assert.That(win.GetPanes(), Does.Contain(win.ActivePane));
    }

    [Test]
    public void NextPane_CyclesToNext()
    {
        using var win = new Window("test", 80, 24);
        var pane1 = win.ActivePane;
        win.SplitPane(SplitDirection.Horizontal);
        win.ActivePane = pane1;
        win.NextPane();
        Assert.That(win.ActivePane, Is.Not.EqualTo(pane1));
    }

    [Test]
    public void NextPane_SinglePane_StaysSame()
    {
        using var win = new Window("test", 80, 24);
        var pane = win.ActivePane;
        win.NextPane();
        Assert.That(win.ActivePane, Is.EqualTo(pane));
    }

    [Test]
    public void PrevPane_CyclesToPrevious()
    {
        using var win = new Window("test", 80, 24);
        win.SplitPane(SplitDirection.Horizontal);
        // Now on second pane
        var pane2 = win.GetPanes()[1];
        win.ActivePane = pane2;
        win.PrevPane();
        Assert.That(win.ActivePane, Is.EqualTo(win.GetPanes()[0]));
    }

    [Test]
    public void Resize_UpdatesDimensions()
    {
        using var win = new Window("test", 80, 24);
        win.Resize(120, 40);
        Assert.That(win.Width, Is.EqualTo(120));
        Assert.That(win.Height, Is.EqualTo(40));
    }

    [Test]
    public void NavigatePane_MovesFocusInDirection()
    {
        using var win = new Window("test", 80, 24);
        var newPane = win.SplitPane(SplitDirection.Horizontal);
        win.ActivePane = win.GetPanes()[0]; // Top pane
        win.NavigatePane(ConsoleKey.DownArrow);
        Assert.That(win.ActivePane, Is.EqualTo(newPane));
    }

    [Test]
    public void NavigatePane_NoMatchingPane_StaysSame()
    {
        using var win = new Window("test", 80, 24);
        win.SplitPane(SplitDirection.Horizontal);
        win.ActivePane = win.GetPanes()[0]; // Top pane
        win.NavigatePane(ConsoleKey.UpArrow); // Nothing above
        Assert.That(win.ActivePane, Is.EqualTo(win.GetPanes()[0]));
    }

    [Test]
    public void CycleLayout_DoesNotThrow()
    {
        using var win = new Window("test", 80, 24);
        win.SplitPane(SplitDirection.Horizontal);
        Assert.DoesNotThrow(() => win.CycleLayout());
    }

}
