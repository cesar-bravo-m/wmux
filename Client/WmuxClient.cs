using System.IO.Pipes;
using Wmux.Commands;
using Wmux.Config;
using Wmux.Core;
using Wmux.Server;
using Wmux.Terminal;
using Wmux.UI;

namespace Wmux.Client;

/// <summary>
/// The wmux client. In standalone mode, runs everything in-process.
/// In client-server mode, connects to the server via named pipe.
/// </summary>
public class WmuxClient
{
    private readonly WmuxConfig _config;
    private readonly KeyBindings _keys;
    private readonly CommandLine _commandLine = new();
    private readonly CommandRegistry _commands = new();
    private Renderer? _renderer;
    private InputHandler? _inputHandler;
    private Win32InputReader? _inputReader;
    private Session? _session;
    private volatile bool _running;
    private volatile bool _needsRender;
    private readonly object _renderLock = new();
    private string? _statusMessage;
    private DateTime _statusExpiry;

    // Client-server mode
    private NamedPipeClientStream? _pipe;
    private bool _serverMode;

    public WmuxClient(WmuxConfig config)
    {
        _config = config;
        _keys = config.Keys;
    }

    /// <summary>
    /// Run in standalone mode (no server - everything in-process).
    /// This is the default when launching "wmux" with no arguments.
    /// </summary>
    public void RunStandalone(string? sessionName = null)
    {
        _serverMode = false;
        int width = Console.WindowWidth;
        int height = Console.WindowHeight;

        sessionName ??= "main";
        _session = new Session(sessionName, width, height - 1);
        _renderer = new Renderer(width, height);
        _inputHandler = new InputHandler(_keys);

        _inputHandler.RequestExit += () => _running = false;
        _inputHandler.RequestDetach += () => _running = false;

        // Wire up output events
        WireOutputEvents();

        RunLoop();
    }

    /// <summary>
    /// Connect to a running server and attach to a session.
    /// </summary>
    public void AttachToServer(string? sessionName = null)
    {
        _serverMode = true;
        _pipe = new NamedPipeClientStream(".", WmuxServer.PipeName, PipeDirection.InOut);

        try
        {
            _pipe.Connect(3000);
        }
        catch
        {
            Console.Error.WriteLine("Error: cannot connect to wmux server. Start one with 'wmux start-server'.");
            return;
        }

        int width = Console.WindowWidth;
        int height = Console.WindowHeight;
        _renderer = new Renderer(width, height);
        _inputHandler = new InputHandler(_keys);
        _inputHandler.RequestDetach += () =>
        {
            IpcProtocol.Send(_pipe, new DetachMessage());
            _running = false;
        };
        _inputHandler.RequestExit += () => _running = false;

        // Send attach or new-session
        if (sessionName != null)
        {
            IpcProtocol.Send(_pipe, new AttachMessage { SessionName = sessionName });
        }
        else
        {
            IpcProtocol.Send(_pipe, new NewSessionMessage
            {
                Name = "main",
                Width = width,
                Height = height
            });
        }

        // In server mode, we need a local session shadow for rendering
        _session = new Session(sessionName ?? "main", width, height - 1);
        WireOutputEvents();
        RunLoop();
    }

    private void RunLoop()
    {
        _running = true;

        // Put the console into raw mode so control keys (Ctrl+B etc.)
        // are delivered without echoing ^B or being intercepted by the OS.
        RawConsole.Enable();

        // Enable alternate screen buffer and hide cursor
        Console.Write("\x1b[?1049h"); // Alt screen buffer
        Console.Write("\x1b[?25l");   // Hide cursor initially
        Console.CursorVisible = false;

        // Initial render
        _needsRender = true;

        // Create the low-level input reader. This uses Win32
        // ReadConsoleInput to read KEY_EVENT_RECORD structs directly
        // from the console input buffer, completely bypassing
        // Console.ReadKey which can echo control characters.
        _inputReader = new Win32InputReader();

        // Handle resize events delivered via WINDOW_BUFFER_SIZE_EVENT.
        // This replaces the old polling-based ResizeLoop.
        _inputReader.WindowResized += (w, h) =>
        {
            // Windows reports the buffer size, not the window size.
            // Use Console.WindowWidth/Height for the actual viewport.
            int actualW = Console.WindowWidth;
            int actualH = Console.WindowHeight;
            lock (_renderLock)
            {
                _session?.Resize(actualW, actualH - 1);
                _renderer?.Resize(actualW, actualH);
            }

            if (_serverMode && _pipe != null)
            {
                IpcProtocol.Send(_pipe, new ResizeMessage { Width = actualW, Height = actualH });
            }

            _needsRender = true;
        };

        // Render thread
        var renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "Renderer" };
        renderThread.Start();

        // Resize detection thread (fallback for terminals that don't
        // deliver WINDOW_BUFFER_SIZE_EVENT reliably)
        var resizeThread = new Thread(ResizeLoop) { IsBackground = true, Name = "ResizeDetect" };
        resizeThread.Start();

        try
        {
            // Main thread: read input using low-level Win32 API.
            // ReadConsoleInput blocks until an event is available,
            // so there is no busy-wait or polling overhead.
            while (_running)
            {
                var key = _inputReader.ReadKey();
                if (key == null)
                {
                    // ReadKey returns null when disposed or on error
                    if (!_running) break;
                    Thread.Sleep(5);
                    continue;
                }

                ProcessInput(key.Value);
            }
        }
        finally
        {
            _inputReader.Dispose();

            // Restore console mode before anything else so the reset
            // sequences are interpreted correctly by a cooked-mode console.
            RawConsole.Restore();

            // Restore terminal
            Console.Write("\x1b[?1049l"); // Main screen buffer
            Console.Write("\x1b[?25h");   // Show cursor
            Console.CursorVisible = true;
            Console.ResetColor();

            _session?.Dispose();
        }
    }

    private void ProcessInput(ConsoleKeyInfo key)
    {
        if (_session == null || _inputHandler == null) return;

        bool consumed = _inputHandler.HandleKey(key, _session, _commandLine, out string? command);

        if (command != null)
        {
            ExecuteCommand(command);
        }

        if (!consumed)
        {
            // Forward to active pane
            var vtSeq = InputHandler.KeyToVtSequence(key);
            if (vtSeq.Length > 0)
            {
                if (_serverMode && _pipe != null)
                {
                    IpcProtocol.Send(_pipe, new InputMessage { Data = vtSeq });
                }
                else
                {
                    _session.ActiveWindow.ActivePane.WriteInput(vtSeq);
                }
            }
        }

        _needsRender = true;
    }

    private void ExecuteCommand(string commandStr)
    {
        var parsed = CommandParser.Parse(commandStr);
        if (parsed == null) return;

        if (_serverMode && _pipe != null)
        {
            IpcProtocol.Send(_pipe, new CommandMessage { Command = commandStr });
        }
        else if (_session != null)
        {
            var result = _commands.Execute(parsed, _session);
            if (result != null)
            {
                _statusMessage = result;
                _statusExpiry = DateTime.Now.AddSeconds(3);
            }
            // Re-wire output events in case new panes were created
            WireOutputEvents();
        }
    }

    private void RenderLoop()
    {
        while (_running)
        {
            if (_needsRender && _session != null && _renderer != null)
            {
                _needsRender = false;
                try
                {
                    string? cmdInput = _commandLine.IsActive ? _commandLine.Input : null;
                    if (_statusMessage != null && DateTime.Now < _statusExpiry)
                        cmdInput ??= _statusMessage;
                    else
                        _statusMessage = null;

                    lock (_renderLock)
                    {
                        _renderer.Render(_session, cmdInput);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Render error: {ex.Message}");
                }
            }
            Thread.Sleep(16); // ~60fps cap
        }
    }

    private void ResizeLoop()
    {
        int lastWidth = Console.WindowWidth;
        int lastHeight = Console.WindowHeight;

        while (_running)
        {
            Thread.Sleep(100);
            int w = Console.WindowWidth;
            int h = Console.WindowHeight;
            if (w != lastWidth || h != lastHeight)
            {
                lastWidth = w;
                lastHeight = h;
                lock (_renderLock)
                {
                    _session?.Resize(w, h - 1);
                    _renderer?.Resize(w, h);
                }

                if (_serverMode && _pipe != null)
                {
                    IpcProtocol.Send(_pipe, new ResizeMessage { Width = w, Height = h });
                }

                _needsRender = true;
            }
        }
    }

    private void WireOutputEvents()
    {
        if (_session == null) return;
        foreach (var win in _session.Windows)
        {
            foreach (var pane in win.GetPanes())
            {
                pane.OutputReceived += _ => _needsRender = true;
            }
        }
    }
}
