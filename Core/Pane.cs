using Wmux.Terminal;

namespace Wmux.Core;

/// <summary>
/// Represents a single pane within a window. Each pane owns a ConPTY process
/// and a virtual screen buffer.
/// </summary>
public class Pane : IDisposable
{
    private static int _nextId = 1;

    public int Id { get; }
    public string Name { get; set; } = "";
    public ConPtyProcess Process { get; }
    public ScreenBuffer Screen { get; private set; }
    public VtParser Parser { get; } = new();
    public ScrollbackBuffer Scrollback { get; } = new();

    // Layout position within the window (set by PaneLayout)
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public bool HasExited => Process.HasExited;

    // Selection (copy) mode state
    public bool IsInSelectionMode { get; set; }
    public int SelectionCursorRow { get; set; }
    public int SelectionCursorCol { get; set; }
    public int SelectionScrollOffset { get; set; }

    // Selection highlight state (set when SPACE pressed to start highlighting)
    public bool SelectionHighlightActive { get; set; }
    public int SelectionAnchorVirtualRow { get; set; }
    public int SelectionAnchorCol { get; set; }

    private Thread? _readThread;
    private volatile bool _running;
    private readonly object _lock = new();

    public event Action<Pane>? OutputReceived;

    /// <summary>
    /// Fired when the child process exits (e.g. user typed "exit").
    /// </summary>
    public event Action<Pane>? ProcessExited;

    public Pane(int left, int top, int width, int height)
    {
        Id = Interlocked.Increment(ref _nextId);
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        Screen = new ScreenBuffer(width, height);
        Screen.Scrollback = Scrollback;
        Process = new ConPtyProcess((short)width, (short)height);
    }

    public void Start()
    {
        _running = true;
        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = $"Pane-{Id}-Read" };
        _readThread.Start();
    }

    private void ReadLoop()
    {
        var buffer = new byte[4096];
        var charBuf = new char[4096];
        // Use a Decoder to correctly handle multi-byte UTF-8 sequences
        // that may be split across read boundaries.
        var decoder = System.Text.Encoding.UTF8.GetDecoder();
        try
        {
            while (_running && !Process.HasExited)
            {
                int bytesRead = Process.OutputStream.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0)
                {
                    // Pipe closed. If the main process is still alive (e.g. a
                    // nested child like WSL/bash exited but the shell continues),
                    // wait for the actual process to exit before reporting.
                    WaitForProcessExit();
                    break;
                }

                int charCount = decoder.GetChars(buffer, 0, bytesRead, charBuf, 0);
                if (charCount > 0)
                {
                    lock (_lock)
                    {
                        Parser.Process(Screen, charBuf.AsSpan(0, charCount));
                    }
                    OutputReceived?.Invoke(this);
                }
            }
        }
        catch (IOException)
        {
            WaitForProcessExit();
        }
        catch (ObjectDisposedException) { }

        // Notify that the child process has exited
        ProcessExited?.Invoke(this);
    }

    /// <summary>
    /// Block until the main shell process actually exits or the pane is disposed.
    /// Called when the ConPTY pipe closes prematurely (e.g. a nested process
    /// like WSL/bash exits while the parent shell is still running).
    /// </summary>
    private void WaitForProcessExit()
    {
        while (_running && !Process.HasExited)
            Thread.Sleep(100);
    }

    public void Resize(int left, int top, int width, int height)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        lock (_lock)
        {
            Screen.Resize(width, height);
        }
        Process.Resize((short)width, (short)height);
    }

    public void WriteInput(string text) => Process.WriteInput(text);

    public void Lock(Action<ScreenBuffer> action)
    {
        lock (_lock) { action(Screen); }
    }

    public void EnterSelectionMode()
    {
        IsInSelectionMode = true;
        SelectionCursorRow = Screen.CursorRow;
        SelectionCursorCol = Screen.CursorCol;
        SelectionScrollOffset = 0;
    }

    public void ExitSelectionMode()
    {
        IsInSelectionMode = false;
        SelectionHighlightActive = false;
        SelectionScrollOffset = 0;
    }

    /// <summary>
    /// Start highlighting from the current cursor position.
    /// Called on first SPACE press in selection mode.
    /// </summary>
    public void StartSelectionHighlight()
    {
        SelectionHighlightActive = true;
        SelectionAnchorVirtualRow = Scrollback.Count - SelectionScrollOffset + SelectionCursorRow;
        SelectionAnchorCol = SelectionCursorCol;
    }

    /// <summary>
    /// Extract the selected text between anchor and current cursor position.
    /// Runs under the pane lock for thread safety.
    /// </summary>
    public string ExtractSelectedText()
    {
        string result = "";
        Lock(screen =>
        {
            int scrollbackCount = Scrollback.Count;
            int cursorVirtualRow = scrollbackCount - SelectionScrollOffset + SelectionCursorRow;
            int cursorCol = SelectionCursorCol;

            // Normalize so start is before end
            int startVR, startCol, endVR, endCol;
            if (SelectionAnchorVirtualRow < cursorVirtualRow ||
                (SelectionAnchorVirtualRow == cursorVirtualRow && SelectionAnchorCol <= cursorCol))
            {
                startVR = SelectionAnchorVirtualRow;
                startCol = SelectionAnchorCol;
                endVR = cursorVirtualRow;
                endCol = cursorCol;
            }
            else
            {
                startVR = cursorVirtualRow;
                startCol = cursorCol;
                endVR = SelectionAnchorVirtualRow;
                endCol = SelectionAnchorCol;
            }

            var lines = new List<string>();
            for (int vr = startVR; vr <= endVR; vr++)
            {
                int colStart = (vr == startVR) ? startCol : 0;
                int colEnd = (vr == endVR) ? endCol : Width - 1;

                var lineSb = new System.Text.StringBuilder();
                if (vr < scrollbackCount)
                {
                    var line = Scrollback.GetLine(vr);
                    if (line.HasValue)
                    {
                        for (int c = colStart; c <= Math.Min(colEnd, line.Value.Chars.Length - 1); c++)
                            lineSb.Append(line.Value.Chars[c]);
                    }
                }
                else
                {
                    int screenRow = vr - scrollbackCount;
                    if (screenRow >= 0 && screenRow < screen.Height)
                    {
                        for (int c = colStart; c <= Math.Min(colEnd, screen.Width - 1); c++)
                            lineSb.Append(screen.Chars[screenRow][c]);
                    }
                }
                lines.Add(lineSb.ToString().TrimEnd());
            }

            result = string.Join(Environment.NewLine, lines);
        });
        return result;
    }

    public void SelectionMoveUp()
    {
        if (SelectionCursorRow > 0)
            SelectionCursorRow--;
        else if (SelectionScrollOffset < Scrollback.Count)
            SelectionScrollOffset++;
    }

    public void SelectionMoveDown()
    {
        if (SelectionCursorRow < Height - 1)
            SelectionCursorRow++;
        else if (SelectionScrollOffset > 0)
            SelectionScrollOffset--;
    }

    public void SelectionMoveLeft()
    {
        if (SelectionCursorCol > 0)
            SelectionCursorCol--;
    }

    public void SelectionMoveRight()
    {
        if (SelectionCursorCol < Width - 1)
            SelectionCursorCol++;
    }

    public void Dispose()
    {
        _running = false;
        // Dispose process and join read thread on a background task.
        // ClosePseudoConsole and stream disposal can block for up to a
        // second waiting for the child process, which freezes the UI
        // thread when called from interactive pane-close paths.
        var process = Process;
        var readThread = _readThread;
        _readThread = null;
        Task.Run(() =>
        {
            process.Dispose();
            readThread?.Join(2000);
        });
    }
}
