namespace Wmux.Core;

/// <summary>
/// A session contains one or more windows. This is the top-level container,
/// analogous to a tmux session.
/// </summary>
public class Session : IDisposable
{
    private static int _nextId;

    public int Id { get; }
    public string Name { get; set; }
    public List<Window> Windows { get; } = new();
    public Window ActiveWindow { get; set; }
    public DateTime CreatedAt { get; } = DateTime.Now;

    public Session(string name, int width, int height)
    {
        Id = Interlocked.Increment(ref _nextId);
        Name = name;
        var firstWindow = new Window("0", width, height);
        Windows.Add(firstWindow);
        ActiveWindow = firstWindow;
    }

    public Window CreateWindow(int width, int height)
    {
        var win = new Window(Windows.Count.ToString(), width, height);
        Windows.Add(win);
        ActiveWindow = win;
        return win;
    }

    public void CloseWindow(Window window)
    {
        if (Windows.Count <= 1) return;
        int idx = Windows.IndexOf(window);
        Windows.Remove(window);
        window.Dispose();

        if (ActiveWindow == window)
            ActiveWindow = Windows[Math.Min(idx, Windows.Count - 1)];
    }

    public void NextWindow()
    {
        int idx = Windows.IndexOf(ActiveWindow);
        ActiveWindow = Windows[(idx + 1) % Windows.Count];
    }

    public void PrevWindow()
    {
        int idx = Windows.IndexOf(ActiveWindow);
        ActiveWindow = Windows[(idx - 1 + Windows.Count) % Windows.Count];
    }

    public void SelectWindow(int index)
    {
        if (index >= 0 && index < Windows.Count)
            ActiveWindow = Windows[index];
    }

    public bool BreakPane()
    {
        var window = ActiveWindow;
        if (window.GetPanes().Count <= 1) return false;

        var pane = window.ActivePane;
        window.DetachPane(pane);

        var newWindow = new Window(Windows.Count.ToString(), window.Width, window.Height, pane);
        int idx = Windows.IndexOf(window);
        Windows.Insert(idx + 1, newWindow);
        ActiveWindow = newWindow;
        return true;
    }

    public void Resize(int width, int height)
    {
        foreach (var win in Windows)
            win.Resize(width, height);
    }

    public void Dispose()
    {
        foreach (var win in Windows)
            win.Dispose();
    }
}
