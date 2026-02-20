namespace Wmux.Core;

public enum SplitDirection { Horizontal, Vertical }

public enum LayoutPreset { EvenHorizontal, EvenVertical, MainHorizontal, MainVertical, Tiled }

/// <summary>
/// Layout engine for positioning panes within a window area.
/// Uses a binary split tree to track how panes are arranged.
/// </summary>
public class PaneLayout
{
    private LayoutNode _root;

    public PaneLayout(Pane initialPane)
    {
        _root = new LayoutNode(initialPane);
    }

    public Pane? Split(Pane target, SplitDirection direction, int totalWidth, int totalHeight)
    {
        var node = FindNode(_root, target);
        if (node == null) return null;

        // Calculate new sizes
        int newWidth, newHeight, newLeft, newTop;

        if (direction == SplitDirection.Vertical)
        {
            int halfWidth = target.Width / 2;
            if (halfWidth < 4) return null; // Too narrow to split

            newWidth = target.Width - halfWidth - 1; // -1 for border
            newHeight = target.Height;
            newLeft = target.Left + halfWidth + 1;
            newTop = target.Top;

            target.Resize(target.Left, target.Top, halfWidth, target.Height);
        }
        else // Horizontal
        {
            int halfHeight = target.Height / 2;
            if (halfHeight < 2) return null; // Too short to split

            newWidth = target.Width;
            newHeight = target.Height - halfHeight - 1; // -1 for border
            newLeft = target.Left;
            newTop = target.Top + halfHeight + 1;

            target.Resize(target.Left, target.Top, target.Width, halfHeight);
        }

        var newPane = new Pane(newLeft, newTop, newWidth, newHeight);
        newPane.Start();

        // Replace the leaf node with a branch
        node.Direction = direction;
        node.First = new LayoutNode(target);
        node.Second = new LayoutNode(newPane);
        node.Pane = null;

        return newPane;
    }

    public bool Remove(Pane target)
    {
        return RemoveNode(_root, null, target);
    }

    private bool RemoveNode(LayoutNode node, LayoutNode? parent, Pane target)
    {
        if (node.IsLeaf)
        {
            if (node.Pane != target) return false;
            if (parent == null) return false; // Can't remove the root pane

            // Find sibling and promote it
            var sibling = parent.First!.Pane == target ? parent.Second! : parent.First!;

            parent.Pane = sibling.Pane;
            parent.First = sibling.First;
            parent.Second = sibling.Second;
            parent.Direction = sibling.Direction;

            return true;
        }

        return RemoveNode(node.First!, node, target) || RemoveNode(node.Second!, node, target);
    }

    public void Recalculate(int left, int top, int width, int height)
    {
        RecalcNode(_root, left, top, width, height);
    }

    private void RecalcNode(LayoutNode node, int left, int top, int width, int height)
    {
        if (node.IsLeaf)
        {
            node.Pane!.Resize(left, top, width, height);
            return;
        }

        if (node.Direction == SplitDirection.Vertical)
        {
            int half = width / 2;
            RecalcNode(node.First!, left, top, half, height);
            RecalcNode(node.Second!, left + half + 1, top, width - half - 1, height);
        }
        else
        {
            int half = height / 2;
            RecalcNode(node.First!, left, top, width, half);
            RecalcNode(node.Second!, left, top + half + 1, width, height - half - 1);
        }
    }

    public List<Pane> GetAllPanes()
    {
        var panes = new List<Pane>();
        CollectPanes(_root, panes);
        return panes;
    }

    private void CollectPanes(LayoutNode node, List<Pane> panes)
    {
        if (node.IsLeaf)
        {
            panes.Add(node.Pane!);
            return;
        }
        CollectPanes(node.First!, panes);
        CollectPanes(node.Second!, panes);
    }

    public void ApplyPreset(LayoutPreset preset, int left, int top, int width, int height)
    {
        var panes = GetAllPanes();
        if (panes.Count <= 1) return;

        switch (preset)
        {
            case LayoutPreset.EvenHorizontal:
                DistributeEvenly(panes, SplitDirection.Horizontal, left, top, width, height);
                break;
            case LayoutPreset.EvenVertical:
                DistributeEvenly(panes, SplitDirection.Vertical, left, top, width, height);
                break;
            case LayoutPreset.Tiled:
                DistributeTiled(panes, left, top, width, height);
                break;
            case LayoutPreset.MainHorizontal:
                DistributeMainH(panes, left, top, width, height);
                break;
            case LayoutPreset.MainVertical:
                DistributeMainV(panes, left, top, width, height);
                break;
        }
    }

    private void DistributeEvenly(List<Pane> panes, SplitDirection dir, int l, int t, int w, int h)
    {
        int n = panes.Count;
        if (dir == SplitDirection.Horizontal)
        {
            int eachH = (h - (n - 1)) / n;
            int curTop = t;
            for (int i = 0; i < n; i++)
            {
                int pH = (i == n - 1) ? (t + h - curTop) : eachH;
                panes[i].Resize(l, curTop, w, pH);
                curTop += pH + 1;
            }
        }
        else
        {
            int eachW = (w - (n - 1)) / n;
            int curLeft = l;
            for (int i = 0; i < n; i++)
            {
                int pW = (i == n - 1) ? (l + w - curLeft) : eachW;
                panes[i].Resize(curLeft, t, pW, h);
                curLeft += pW + 1;
            }
        }
    }

    private void DistributeTiled(List<Pane> panes, int l, int t, int w, int h)
    {
        int n = panes.Count;
        int cols = (int)Math.Ceiling(Math.Sqrt(n));
        int rows = (int)Math.Ceiling((double)n / cols);

        int idx = 0;
        int cellH = (h - (rows - 1)) / rows;
        int curTop = t;

        for (int r = 0; r < rows && idx < n; r++)
        {
            int rowPanes = Math.Min(cols, n - idx);
            int cellW = (w - (rowPanes - 1)) / rowPanes;
            int curLeft = l;
            int pH = (r == rows - 1) ? (t + h - curTop) : cellH;

            for (int c = 0; c < rowPanes && idx < n; c++, idx++)
            {
                int pW = (c == rowPanes - 1) ? (l + w - curLeft) : cellW;
                panes[idx].Resize(curLeft, curTop, pW, pH);
                curLeft += pW + 1;
            }
            curTop += pH + 1;
        }
    }

    private void DistributeMainH(List<Pane> panes, int l, int t, int w, int h)
    {
        if (panes.Count == 1) { panes[0].Resize(l, t, w, h); return; }
        int mainH = h / 2;
        panes[0].Resize(l, t, w, mainH);
        var rest = panes.Skip(1).ToList();
        DistributeEvenly(rest, SplitDirection.Vertical, l, t + mainH + 1, w, h - mainH - 1);
    }

    private void DistributeMainV(List<Pane> panes, int l, int t, int w, int h)
    {
        if (panes.Count == 1) { panes[0].Resize(l, t, w, h); return; }
        int mainW = w / 2;
        panes[0].Resize(l, t, mainW, h);
        var rest = panes.Skip(1).ToList();
        DistributeEvenly(rest, SplitDirection.Horizontal, l + mainW + 1, t, w - mainW - 1, h);
    }

    private LayoutNode? FindNode(LayoutNode node, Pane target)
    {
        if (node.IsLeaf)
            return node.Pane == target ? node : null;
        return FindNode(node.First!, target) ?? FindNode(node.Second!, target);
    }

    private class LayoutNode
    {
        public Pane? Pane;
        public LayoutNode? First;
        public LayoutNode? Second;
        public SplitDirection Direction;
        public bool IsLeaf => Pane != null;

        public LayoutNode(Pane pane) { Pane = pane; }
    }
}
