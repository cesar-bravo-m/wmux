namespace Wmux.Core;

/// <summary>
/// Ring buffer storing scrollback history lines for a pane.
/// </summary>
public class ScrollbackBuffer
{
    private readonly string[] _lines;
    private int _head;
    private int _count;

    public int Capacity { get; }
    public int Count => _count;

    public ScrollbackBuffer(int capacity = 10000)
    {
        Capacity = capacity;
        _lines = new string[capacity];
    }

    public void Add(string line)
    {
        _lines[_head] = line;
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    public string? GetLine(int index)
    {
        if (index < 0 || index >= _count) return null;
        int actual = (_head - _count + index + Capacity) % Capacity;
        return _lines[actual];
    }

    public void Clear()
    {
        _head = 0;
        _count = 0;
    }
}
