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
            int firstW = Math.Clamp((int)(width * node.Ratio), 1, width - 2);
            RecalcNode(node.First!, left, top, firstW, height);
            RecalcNode(node.Second!, left + firstW + 1, top, width - firstW - 1, height);
        }
        else
        {
            int firstH = Math.Clamp((int)(height * node.Ratio), 1, height - 2);
            RecalcNode(node.First!, left, top, width, firstH);
            RecalcNode(node.Second!, left, top + firstH + 1, width, height - firstH - 1);
        }
    }

    public bool ResizePane(Pane target, ConsoleKey direction, int amount, int totalWidth, int totalHeight)
    {
        var path = new List<(LayoutNode node, bool wentFirst, int width, int height)>();
        if (!BuildPathToLeaf(_root, target, totalWidth, totalHeight, path))
            return false;

        SplitDirection wantDir = direction is ConsoleKey.LeftArrow or ConsoleKey.RightArrow
            ? SplitDirection.Vertical : SplitDirection.Horizontal;
        bool increase = direction is ConsoleKey.RightArrow or ConsoleKey.DownArrow;

        for (int i = path.Count - 1; i >= 0; i--)
        {
            var (node, _, w, h) = path[i];
            if (node.Direction != wantDir) continue;

            int dim = wantDir == SplitDirection.Vertical ? w : h;
            double delta = (double)amount / dim;
            node.Ratio = increase
                ? Math.Clamp(node.Ratio + delta, 0.1, 0.9)
                : Math.Clamp(node.Ratio - delta, 0.1, 0.9);
            return true;
        }
        return false;
    }

    public void EqualizeAll()
    {
        ResetRatios(_root);
    }

    private void ResetRatios(LayoutNode node)
    {
        if (node.IsLeaf) return;
        node.Ratio = 0.5;
        ResetRatios(node.First!);
        ResetRatios(node.Second!);
    }

    private bool BuildPathToLeaf(LayoutNode node, Pane target, int w, int h,
        List<(LayoutNode, bool, int, int)> path)
    {
        if (node.IsLeaf)
            return node.Pane == target;

        int firstW, firstH, secondW, secondH;
        if (node.Direction == SplitDirection.Vertical)
        {
            firstW = Math.Clamp((int)(w * node.Ratio), 1, w - 2);
            firstH = h;
            secondW = w - firstW - 1;
            secondH = h;
        }
        else
        {
            firstW = w;
            firstH = Math.Clamp((int)(h * node.Ratio), 1, h - 2);
            secondW = w;
            secondH = h - firstH - 1;
        }

        path.Add((node, true, w, h));
        if (BuildPathToLeaf(node.First!, target, firstW, firstH, path))
            return true;
        path.RemoveAt(path.Count - 1);

        path.Add((node, false, w, h));
        if (BuildPathToLeaf(node.Second!, target, secondW, secondH, path))
            return true;
        path.RemoveAt(path.Count - 1);

        return false;
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
        public double Ratio = 0.5;
        public bool IsLeaf => Pane != null;

        public LayoutNode(Pane pane) { Pane = pane; }
    }
}
