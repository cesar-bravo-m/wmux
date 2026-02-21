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
                if (bytesRead <= 0) break;

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
        catch (IOException) { }
        catch (ObjectDisposedException) { }

        // Notify that the child process has exited
        ProcessExited?.Invoke(this);
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
