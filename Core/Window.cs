namespace Wmux.Core;

/// <summary>
/// A window contains one or more panes arranged by a layout engine.
/// </summary>
public class Window : IDisposable
{
    private static int _nextId;

    public int Id { get; }
    public string Name { get; set; }
    public PaneLayout Layout { get; private set; }
    public Pane ActivePane { get; set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    private LayoutPreset _currentPreset = LayoutPreset.Tiled;
    private int _presetIndex;

    public Window(string name, int width, int height)
    {
        Id = Interlocked.Increment(ref _nextId);
        Name = name;
        Width = width;
        Height = height;

        var firstPane = new Pane(0, 0, width, height);
        firstPane.Start();
        Layout = new PaneLayout(firstPane);
        ActivePane = firstPane;
    }

    public List<Pane> GetPanes() => Layout.GetAllPanes();

    public Pane? SplitPane(SplitDirection direction)
    {
        var newPane = Layout.Split(ActivePane, direction, Width, Height);
        return newPane;
    }

    public void ClosePane(Pane pane)
    {
        var panes = GetPanes();
        if (panes.Count <= 1) return;

        if (Layout.Remove(pane))
        {
            pane.Dispose();
            Layout.Recalculate(0, 0, Width, Height);

            if (ActivePane == pane)
                ActivePane = GetPanes().First();
        }
    }

    public void Resize(int width, int height)
    {
        Width = width;
        Height = height;
        Layout.Recalculate(0, 0, width, height);
    }

    public void NextPane()
    {
        var panes = GetPanes();
        int idx = panes.IndexOf(ActivePane);
        ActivePane = panes[(idx + 1) % panes.Count];
    }

    public void PrevPane()
    {
        var panes = GetPanes();
        int idx = panes.IndexOf(ActivePane);
        ActivePane = panes[(idx - 1 + panes.Count) % panes.Count];
    }

    public void NavigatePane(ConsoleKey direction)
    {
        var panes = GetPanes();
        Pane? best = null;
        int bestDist = int.MaxValue;

        int cx = ActivePane.Left + ActivePane.Width / 2;
        int cy = ActivePane.Top + ActivePane.Height / 2;

        foreach (var p in panes)
        {
            if (p == ActivePane) continue;
            int px = p.Left + p.Width / 2;
            int py = p.Top + p.Height / 2;

            bool valid = direction switch
            {
                ConsoleKey.UpArrow => py < cy,
                ConsoleKey.DownArrow => py > cy,
                ConsoleKey.LeftArrow => px < cx,
                ConsoleKey.RightArrow => px > cx,
                _ => false
            };

            if (!valid) continue;
            int dist = Math.Abs(px - cx) + Math.Abs(py - cy);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = p;
            }
        }

        if (best != null)
            ActivePane = best;
    }

    public void CycleLayout()
    {
        var presets = Enum.GetValues<LayoutPreset>();
        _presetIndex = (_presetIndex + 1) % presets.Length;
        _currentPreset = presets[_presetIndex];
        Layout.ApplyPreset(_currentPreset, 0, 0, Width, Height);
    }

    public void Dispose()
    {
        foreach (var pane in GetPanes())
            pane.Dispose();
    }
}
