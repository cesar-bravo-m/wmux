namespace Wmux.Core;

/// <summary>
/// A single scrollback line with character and color data.
/// </summary>
public record struct ScrollbackLine(char[] Chars, ConsoleColor[] Fg, ConsoleColor[] Bg);

/// <summary>
/// Ring buffer storing scrollback history lines for a pane.
/// Each line preserves the characters and foreground/background colors.
/// </summary>
public class ScrollbackBuffer
{
    private readonly ScrollbackLine[] _lines;
    private int _head;
    private int _count;

    public int Capacity { get; }
    public int Count => _count;

    public ScrollbackBuffer(int capacity = 10000)
    {
        Capacity = capacity;
        _lines = new ScrollbackLine[capacity];
    }

    public void Add(ScrollbackLine line)
    {
        _lines[_head] = line;
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    /// <summary>
    /// Convenience overload for adding a plain text line with default colors.
    /// </summary>
    public void Add(string text)
    {
        var chars = text.ToCharArray();
        var fg = new ConsoleColor[chars.Length];
        var bg = new ConsoleColor[chars.Length];
        Array.Fill(fg, ConsoleColor.Gray);
        Array.Fill(bg, ConsoleColor.Black);
        Add(new ScrollbackLine(chars, fg, bg));
    }

    public ScrollbackLine? GetLine(int index)
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
