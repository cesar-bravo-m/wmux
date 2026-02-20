using Wmux.Core;

namespace Wmux.Tests;

/// <summary>
/// Tests for PaneLayout binary split tree engine.
/// Note: Creates real Pane objects (which spawn ConPTY processes).
/// </summary>
[TestFixture]
[Platform("Win")]
public class PaneLayoutTests
{
    [Test]
    public void Constructor_SinglePane()
    {
        using var pane = CreatePane(0, 0, 80, 24);
        var layout = new PaneLayout(pane);
        var panes = layout.GetAllPanes();

        Assert.That(panes, Has.Count.EqualTo(1));
        Assert.That(panes[0], Is.EqualTo(pane));
    }

    [Test]
    public void Split_Vertical_CreatesTwoPanes()
    {
        using var pane = CreatePane(0, 0, 80, 24);
        var layout = new PaneLayout(pane);

        var newPane = layout.Split(pane, SplitDirection.Vertical, 80, 24);

        Assert.That(newPane, Is.Not.Null);
        var panes = layout.GetAllPanes();
        Assert.That(panes, Has.Count.EqualTo(2));

        // Original should have half width
        Assert.That(pane.Width, Is.EqualTo(40));
        // New pane on the right
        Assert.That(newPane!.Left, Is.EqualTo(41));
        newPane.Dispose();
    }

    [Test]
    public void Split_Horizontal_CreatesTwoPanes()
    {
        using var pane = CreatePane(0, 0, 80, 24);
        var layout = new PaneLayout(pane);

        var newPane = layout.Split(pane, SplitDirection.Horizontal, 80, 24);

        Assert.That(newPane, Is.Not.Null);
        var panes = layout.GetAllPanes();
        Assert.That(panes, Has.Count.EqualTo(2));

        Assert.That(pane.Height, Is.EqualTo(12));
        Assert.That(newPane!.Top, Is.EqualTo(13));
        newPane.Dispose();
    }

    [Test]
    public void Split_TooNarrow_ReturnsNull()
    {
        using var pane = CreatePane(0, 0, 6, 24);
        var layout = new PaneLayout(pane);

        var result = layout.Split(pane, SplitDirection.Vertical, 6, 24);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Split_TooShort_ReturnsNull()
    {
        using var pane = CreatePane(0, 0, 80, 3);
        var layout = new PaneLayout(pane);

        var result = layout.Split(pane, SplitDirection.Horizontal, 80, 3);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Split_MultipleSplits()
    {
        using var pane = CreatePane(0, 0, 80, 24);
        var layout = new PaneLayout(pane);

        var p2 = layout.Split(pane, SplitDirection.Vertical, 80, 24);
        Assert.That(p2, Is.Not.Null);
        var p3 = layout.Split(pane, SplitDirection.Horizontal, 80, 24);
        Assert.That(p3, Is.Not.Null);

        var panes = layout.GetAllPanes();
        Assert.That(panes, Has.Count.EqualTo(3));

        p2!.Dispose();
        p3!.Dispose();
    }

    [Test]
    public void Remove_RemovesPane()
    {
        using var pane = CreatePane(0, 0, 80, 24);
        var layout = new PaneLayout(pane);
        var p2 = layout.Split(pane, SplitDirection.Vertical, 80, 24);

        bool removed = layout.Remove(p2!);

        Assert.That(removed, Is.True);
        Assert.That(layout.GetAllPanes(), Has.Count.EqualTo(1));
        p2!.Dispose();
    }

    [Test]
    public void Remove_RootPane_ReturnsFalse()
    {
        using var pane = CreatePane(0, 0, 80, 24);
        var layout = new PaneLayout(pane);

        bool removed = layout.Remove(pane);
        Assert.That(removed, Is.False);
        Assert.That(layout.GetAllPanes(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Remove_NonexistentPane_ReturnsFalse()
    {
        using var pane = CreatePane(0, 0, 80, 24);
        using var other = CreatePane(0, 0, 40, 24);
        var layout = new PaneLayout(pane);

        bool removed = layout.Remove(other);
        Assert.That(removed, Is.False);
    }

    [Test]
    public void Recalculate_UpdatesPanePositions()
    {
        using var pane = CreatePane(0, 0, 80, 24);
        var layout = new PaneLayout(pane);
        var p2 = layout.Split(pane, SplitDirection.Vertical, 80, 24);

        // Resize the overall area
        layout.Recalculate(0, 0, 120, 40);

        Assert.That(pane.Width, Is.EqualTo(60));
        Assert.That(pane.Height, Is.EqualTo(40));
        Assert.That(p2!.Left, Is.EqualTo(61));
        Assert.That(p2.Width, Is.EqualTo(59));
        Assert.That(p2.Height, Is.EqualTo(40));

        p2.Dispose();
    }

    [Test]
    public void Recalculate_HorizontalSplit_UpdatesHeights()
    {
        using var pane = CreatePane(0, 0, 80, 24);
        var layout = new PaneLayout(pane);
        var p2 = layout.Split(pane, SplitDirection.Horizontal, 80, 24);

        layout.Recalculate(0, 0, 80, 40);

        Assert.That(pane.Height, Is.EqualTo(20));
        Assert.That(p2!.Top, Is.EqualTo(21));
        Assert.That(p2.Height, Is.EqualTo(19));

        p2.Dispose();
    }

    [Test]
    public void GetAllPanes_ReturnsAllPanesInOrder()
    {
        using var pane = CreatePane(0, 0, 80, 24);
        var layout = new PaneLayout(pane);
        var p2 = layout.Split(pane, SplitDirection.Vertical, 80, 24);
        var p3 = layout.Split(pane, SplitDirection.Horizontal, 80, 24);

        var panes = layout.GetAllPanes();

        // Should have all three panes
        Assert.That(panes, Has.Count.EqualTo(3));
        Assert.That(panes, Does.Contain(pane));
        Assert.That(panes, Does.Contain(p2));
        Assert.That(panes, Does.Contain(p3));

        p2!.Dispose();
        p3!.Dispose();
    }

    [Test]
    public void ApplyPreset_EvenHorizontal()
    {
        using var pane = CreatePane(0, 0, 80, 24);
        var layout = new PaneLayout(pane);
        var p2 = layout.Split(pane, SplitDirection.Vertical, 80, 24);

        layout.ApplyPreset(LayoutPreset.EvenHorizontal, 0, 0, 80, 21);

        var panes = layout.GetAllPanes();
        // Both panes should be full width, stacked vertically
        Assert.That(panes[0].Width, Is.EqualTo(80));
        Assert.That(panes[1].Width, Is.EqualTo(80));
        // Heights should be roughly equal
        Assert.That(panes[0].Height + panes[1].Height, Is.LessThanOrEqualTo(21));

        p2!.Dispose();
    }

    [Test]
    public void ApplyPreset_EvenVertical()
    {
        using var pane = CreatePane(0, 0, 80, 24);
        var layout = new PaneLayout(pane);
        var p2 = layout.Split(pane, SplitDirection.Horizontal, 80, 24);

        layout.ApplyPreset(LayoutPreset.EvenVertical, 0, 0, 81, 24);

        var panes = layout.GetAllPanes();
        // Both panes should be full height, side by side
        Assert.That(panes[0].Height, Is.EqualTo(24));
        Assert.That(panes[1].Height, Is.EqualTo(24));

        p2!.Dispose();
    }

    [Test]
    public void ApplyPreset_Tiled()
    {
        using var pane = CreatePane(0, 0, 80, 24);
        var layout = new PaneLayout(pane);
        var p2 = layout.Split(pane, SplitDirection.Vertical, 80, 24);
        var p3 = layout.Split(pane, SplitDirection.Horizontal, 80, 24);
        var p4 = layout.Split(p2!, SplitDirection.Horizontal, 80, 24);

        layout.ApplyPreset(LayoutPreset.Tiled, 0, 0, 81, 25);

        var panes = layout.GetAllPanes();
        Assert.That(panes, Has.Count.EqualTo(4));
        // All panes should have positive dimensions
        foreach (var p in panes)
        {
            Assert.That(p.Width, Is.GreaterThan(0));
            Assert.That(p.Height, Is.GreaterThan(0));
        }

        p2!.Dispose();
        p3!.Dispose();
        p4!.Dispose();
    }

    [Test]
    public void ApplyPreset_SinglePane_DoesNothing()
    {
        using var pane = CreatePane(0, 0, 80, 24);
        var layout = new PaneLayout(pane);

        // Should not throw
        Assert.DoesNotThrow(() => layout.ApplyPreset(LayoutPreset.Tiled, 0, 0, 80, 24));
        Assert.That(layout.GetAllPanes(), Has.Count.EqualTo(1));
    }

    private static Pane CreatePane(int left, int top, int width, int height)
    {
        var pane = new Pane(left, top, width, height);
        // Don't call Start()
        return pane;
    }
}
