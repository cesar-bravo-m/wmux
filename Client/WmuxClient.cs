using System.Net.Sockets;
using Wmux.Commands;
using Wmux.Config;
using Wmux.Core;
using Wmux.Server;
using Wmux.Terminal;
using Wmux.UI;

namespace Wmux.Client;

public enum ClientMode
{
    CreateOrAttach,
    ForceCreate,
    Attach,
}

/// <summary>
/// The wmux client. In standalone mode, runs everything in-process.
/// In client-server mode, connects to the server via TCP socket and
/// renders ScreenSnapshotMessages received from the server.
/// </summary>
public class WmuxClient
{
    private readonly WmuxConfig _config;
    private readonly KeyBindings _keys;
    private readonly CommandLine _commandLine = new();
    private readonly CommandRegistry _commands = new();
    private Renderer? _renderer;
    private InputHandler? _inputHandler;
    private Session? _session;
    private volatile bool _running;
    private volatile bool _needsRender;
    private readonly object _renderLock = new();
    private string? _statusMessage;
    private DateTime _statusExpiry;
    private ConsoleColor _statusFg = ConsoleColor.Black;
    private ConsoleColor _statusBg = ConsoleColor.Green;

    // Client-server mode
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private readonly object _streamLock = new();
    private bool _serverMode;
    private ScreenSnapshotMessage? _lastSnapshot;
    private Win32InputReader? _inputReader;
    private string? _fatalError;

    public WmuxClient(WmuxConfig config)
    {
        _config = config;
        _keys = config.Keys;
    }

    // ─────────────────────────────────────────────────────────────
    //  Standalone mode (no server — everything in-process)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Run in standalone mode (no server - everything in-process).
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
        _inputHandler.RequestClosePane += CloseActivePane;
        _inputHandler.StatusMessage += (msg, fg, bg) =>
        {
            _statusMessage = msg;
            _statusFg = fg;
            _statusBg = bg;
            _statusExpiry = DateTime.Now.AddSeconds(2);
            _needsRender = true;
        };

        WireOutputEvents();
        RunLoop();
    }

    // ─────────────────────────────────────────────────────────────
    //  Server mode (thin client)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Connect to a running server and attach to a session.
    /// In this mode the client owns NO Session — it receives pre-composed
    /// screen grids from the server and sends input/commands back.
    /// </summary>
    private static readonly string _diagLog = Path.Combine(Path.GetTempPath(), "wmux-diag.log");
    private static void Diag(string msg)
    {
        try { File.AppendAllText(_diagLog, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
    }

    public void AttachToServer(string? sessionName = null, ClientMode mode = ClientMode.CreateOrAttach, int port = 0)
    {
        File.WriteAllText(_diagLog, $"[{DateTime.Now:HH:mm:ss.fff}] CLIENT AttachToServer mode={mode}\n");
        _serverMode = true;

        if (port <= 0)
            port = WmuxServer.GetServerPort();

        _tcpClient = new TcpClient();
        try
        {
            _tcpClient.Connect("127.0.0.1", port);
            _tcpClient.NoDelay = true;
            _stream = _tcpClient.GetStream();
        }
        catch (Exception ex)
        {
            Diag($"CLIENT Connect FAILED: {ex.Message}");
            Console.Error.WriteLine("Error: cannot connect to wmux server. Start one with 'wmux start-server'.");
            return;
        }
        Diag($"CLIENT Connected OK to port {port}");

        int width = Console.WindowWidth;
        int height = Console.WindowHeight;
        Diag($"CLIENT Terminal size: {width}x{height}");
        _renderer = new Renderer(width, height);
        _inputHandler = new InputHandler(_keys);

        _inputHandler.RequestDetach += () =>
        {
            SendToServer(new DetachMessage());
            _running = false;
        };
        _inputHandler.RequestExit += () => _running = false;

        // No local Session — the server owns all state.
        _session = null;

        Diag($"CLIENT Sending initial message for mode={mode}");
        switch (mode)
        {
            case ClientMode.ForceCreate:
                SendToServer(new NewSessionMessage
                {
                    Name = sessionName ?? "",
                    Width = width,
                    Height = height,
                    ForceCreate = true,
                });
                break;

            case ClientMode.Attach:
                SendToServer(new AttachMessage { SessionName = sessionName });
                SendToServer(new ResizeMessage { Width = width, Height = height });
                break;

            case ClientMode.CreateOrAttach:
            default:
                SendToServer(new NewSessionMessage
                {
                    Name = sessionName ?? "0",
                    Width = width,
                    Height = height,
                    ForceCreate = false,
                });
                break;
        }
        Diag($"CLIENT Initial message sent OK");
        // Set _running before starting the receive thread — otherwise
        // the thread sees _running == false and exits immediately.
        _running = true;

        var receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "ServerReceive" };
        receiveThread.Start();
        Diag($"CLIENT ReceiveLoop thread started, entering RunLoop");

        RunLoop();
    }

    /// <summary>
    /// Synchronous send to the server (thread-safe).
    /// </summary>
    private void SendToServer(IpcMessage message)
    {
        try
        {
            lock (_streamLock)
            {
                if (_stream != null)
                    IpcProtocol.Send(_stream, message);
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    /// <summary>
    /// Receive loop for server mode. Reads messages from the stream and
    /// dispatches them (screen snapshots, command results, errors, etc.).
    /// </summary>
    private void ReceiveLoop()
    {
        Diag($"CLIENT ReceiveLoop started. _running={_running}");
        int msgCount = 0;
        while (_running && _stream != null)
        {
            try
            {
                Diag($"CLIENT ReceiveLoop waiting for message #{msgCount + 1}...");
                var msg = IpcProtocol.Receive(_stream);
                if (msg == null)
                {
                    Diag("CLIENT ReceiveLoop: Receive returned null (connection closed)");
                    break;
                }
                msgCount++;
                Diag($"CLIENT ReceiveLoop: got message #{msgCount} type={msg.GetType().Name}");

                switch (msg)
                {
                    case ScreenSnapshotMessage snapshot:
                        Diag($"CLIENT ReceiveLoop: snapshot {snapshot.Width}x{snapshot.Height} chars={snapshot.Chars.Length}");
                        _lastSnapshot = snapshot;
                        _needsRender = true;
                        break;
                    case CommandResultMessage cr:
                        if (cr.Result != null)
                        {
                            // If selection was copied, exit selection mode on the client
                            if (cr.Result == "selection copied to clipboard")
                                _inputHandler?.ExitSelectionModeExternal();

                            _statusMessage = cr.Result;
                            _statusExpiry = DateTime.Now.AddSeconds(2);
                            if (cr.Result is "No next window" or "No previous window")
                            {
                                _statusFg = ConsoleColor.Black;
                                _statusBg = ConsoleColor.Yellow;
                            }
                            else
                            {
                                _statusFg = ConsoleColor.Black;
                                _statusBg = ConsoleColor.Green;
                            }
                            _needsRender = true;
                        }
                        break;
                    case ErrorMessage err:
                        Diag($"CLIENT ReceiveLoop: ErrorMessage: {err.Text}");
                        // If we've never received a snapshot, this is a fatal
                        // error (e.g. "No matching session found"). Exit cleanly.
                        if (_lastSnapshot == null)
                        {
                            _fatalError = $"Error: {err.Text}";
                            _running = false;
                            _inputReader?.Dispose();
                            return;
                        }
                        _statusMessage = err.Text;
                        _statusExpiry = DateTime.Now.AddSeconds(5);
                        _needsRender = true;
                        break;
                    case SessionClosedMessage:
                        Diag("CLIENT ReceiveLoop: SessionClosed");
                        _running = false;
                        break;
                }
            }
            catch (IOException ex)
            {
                Diag($"CLIENT ReceiveLoop: IOException: {ex.Message}");
                break;
            }
            catch (ObjectDisposedException ex)
            {
                Diag($"CLIENT ReceiveLoop: ObjectDisposedException: {ex.Message}");
                break;
            }
            catch (Exception ex)
            {
                Diag($"CLIENT ReceiveLoop: Exception: {ex.GetType().Name}: {ex.Message}");
                break;
            }
        }

        Diag($"CLIENT ReceiveLoop exited. msgCount={msgCount} _running={_running}");
        // Server disconnected — exit
        _running = false;
        _inputReader?.Dispose();
    }

    // ─────────────────────────────────────────────────────────────
    //  Shared run loop
    // ─────────────────────────────────────────────────────────────

    private void RunLoop()
    {
        Diag("CLIENT RunLoop entered");
        _running = true;

        // Ensure the console uses UTF-8 so Unicode characters are not
        // replaced with '?' by the default OEM code page.
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        // Put the console into raw mode so control keys (Ctrl+B etc.)
        // are delivered without echoing ^B or being intercepted by the OS.
        bool rawOk = RawConsole.Enable();
        Diag($"CLIENT RunLoop: RawConsole.Enable returned {rawOk}");

        // Enable alternate screen buffer and hide cursor
        Console.Write("\x1b[?1049h"); // Alt screen buffer
        Console.Write("\x1b[?25l");   // Hide cursor initially
        Console.CursorVisible = false;
        Diag("CLIENT RunLoop: alt screen enabled");

        // Initial render
        _needsRender = true;

        // Create the low-level input reader
        _inputReader = new Win32InputReader();
        Diag("CLIENT RunLoop: Win32InputReader created, starting render+resize threads");

        // Handle resize events
        _inputReader.WindowResized += (w, h) =>
        {
            int actualW = Console.WindowWidth;
            int actualH = Console.WindowHeight;
            lock (_renderLock)
            {
                _session?.Resize(actualW, actualH - 1);
                _renderer?.Resize(actualW, actualH);
            }

            if (_serverMode && _stream != null)
            {
                SendToServer(new ResizeMessage { Width = actualW, Height = actualH });
            }

            _needsRender = true;
        };

        // Render thread
        var renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "Renderer" };
        renderThread.Start();

        // Resize detection thread (fallback)
        var resizeThread = new Thread(ResizeLoop) { IsBackground = true, Name = "ResizeDetect" };
        resizeThread.Start();

        try
        {
            while (_running)
            {
                // When the activation string is partially or fully typed,
                // use a short timeout so we can flush it as literal text
                // if no command key follows.
                int timeout = _inputHandler != null && _inputHandler.HasPendingPrefix
                    ? InputHandler.PrefixTimeoutMs : -1;

                var key = _inputReader.ReadKey(timeout);
                if (key == null)
                {
                    if (!_running) break;
                    HandlePrefixTimeout();
                    continue;
                }

                ProcessInput(key.Value);
            }
        }
        finally
        {
            _inputReader.Dispose();
            _inputReader = null;

            RawConsole.Restore();

            // Restore terminal
            Console.Write("\x1b[?1049l"); // Main screen buffer
            Console.Write("\x1b[?25h");   // Show cursor
            Console.CursorVisible = true;
            Console.ResetColor();

            _session?.Dispose();
            try { _stream?.Dispose(); } catch { }
            try { _tcpClient?.Dispose(); } catch { }

            // Show any fatal error that caused us to exit
            if (_fatalError != null)
                Console.Error.WriteLine(_fatalError);
        }
    }

    private void ProcessInput(ConsoleKeyInfo key)
    {
        if (_inputHandler == null) return;

        if (_serverMode)
        {
            ProcessInputServerMode(key);
            return;
        }

        // Standalone mode
        if (_session == null) return;

        bool consumed = _inputHandler.HandleKey(key, _session, _commandLine, out string? command);

        // Forward any deferred keys (flushed 'z' from prefix sequence)
        foreach (var deferred in _inputHandler.DeferredKeys)
        {
            var dVt = InputHandler.KeyToVtSequence(deferred);
            if (dVt.Length > 0)
                _session.ActiveWindow.ActivePane.WriteInput(dVt);
        }

        if (command != null)
            ExecuteCommand(command);

        if (!consumed)
        {
            var vtSeq = InputHandler.KeyToVtSequence(key);
            if (vtSeq.Length > 0)
                _session.ActiveWindow.ActivePane.WriteInput(vtSeq);
        }

        if (consumed)
            WireOutputEvents();

        _needsRender = true;
    }

    /// <summary>
    /// Server-mode input handling. Uses HandleKeyServerMode which returns
    /// command strings instead of mutating a Session.
    /// </summary>
    private void ProcessInputServerMode(ConsoleKeyInfo key)
    {
        if (_inputHandler == null) return;

        bool consumed = _inputHandler.HandleKeyServerMode(key, _commandLine, out string? action);

        // Forward any deferred keys (flushed 'z' from prefix sequence)
        foreach (var deferred in _inputHandler.DeferredKeys)
        {
            var dVt = InputHandler.KeyToVtSequence(deferred);
            if (dVt.Length > 0)
                SendToServer(new InputMessage { Data = dVt });
        }

        if (action != null)
            SendToServer(new CommandMessage { Command = action });
        else if (!consumed)
        {
            var vtSeq = InputHandler.KeyToVtSequence(key);
            if (vtSeq.Length > 0)
                SendToServer(new InputMessage { Data = vtSeq });
        }

        // Command mode is client-local state — re-apply the last snapshot
        // so the command line overlay is updated immediately.
        if (consumed)
            _needsRender = true;
    }

    /// <summary>
    /// Called when ReadKey times out while the activation string is
    /// pending or active. Flushes the buffered characters as literal
    /// input to the shell.
    /// </summary>
    private void HandlePrefixTimeout()
    {
        if (_inputHandler == null) return;

        var flushed = _inputHandler.FlushPrefixTimeout();
        if (flushed == null) return;

        if (_serverMode)
        {
            SendToServer(new InputMessage { Data = flushed });
        }
        else if (_session != null)
        {
            _session.ActiveWindow.ActivePane.WriteInput(flushed);
        }

        _needsRender = true;
    }

    private void ExecuteCommand(string commandStr)
    {
        var parsed = CommandParser.Parse(commandStr);
        if (parsed == null) return;

        if (_session != null)
        {
            var result = _commands.Execute(parsed, _session);
            if (result != null)
            {
                _statusMessage = result;
                _statusExpiry = DateTime.Now.AddSeconds(2);
                _statusFg = ConsoleColor.Black;
                _statusBg = ConsoleColor.Green;
            }
            WireOutputEvents();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Render loop
    // ─────────────────────────────────────────────────────────────

    private int _renderCount;
    private void RenderLoop()
    {
        Diag("CLIENT RenderLoop started");
        while (_running)
        {
            if ((_needsRender || _statusMessage != null) && _renderer != null)
            {
                _needsRender = false;
                _renderCount++;
                try
                {
                    if (_serverMode)
                    {
                        if (_renderCount <= 5)
                            Diag($"CLIENT RenderLoop: render #{_renderCount} serverMode=true lastSnapshot={(_lastSnapshot != null ? "SET" : "NULL")}");
                        RenderServerMode();
                    }
                    else if (_session != null)
                    {
                        RenderStandaloneMode();
                    }
                }
                catch (Exception ex)
                {
                    Diag($"CLIENT RenderLoop: exception: {ex.GetType().Name}: {ex.Message}");
                }
            }
            Thread.Sleep(16); // ~60fps cap
        }
        Diag("CLIENT RenderLoop exited");
    }

    private void RenderStandaloneMode()
    {
        if (_session == null || _renderer == null) return;
        if (_session.Windows.Count == 0) return;

        bool cmdMode = _commandLine.IsActive;
        string? cmdInput = cmdMode ? _commandLine.Input : null;
        var sFg = ConsoleColor.Black;
        var sBg = ConsoleColor.Green;
        if (cmdMode)
        {
            sFg = ConsoleColor.Black;
            sBg = ConsoleColor.Yellow;
        }
        else if (_inputHandler != null && _inputHandler.IsSelectionMode)
        {
            cmdInput = "selection mode";
            sFg = ConsoleColor.Black;
            sBg = ConsoleColor.Yellow;
        }
        else if (_inputHandler != null && (_inputHandler.IsPrefixActive || _inputHandler.IsPrefixPending))
        {
            cmdInput = _inputHandler.PrefixProgress;
            sFg = ConsoleColor.White;
            sBg = ConsoleColor.DarkMagenta;
        }
        else if (_statusMessage != null && DateTime.Now < _statusExpiry)
        {
            cmdInput = _statusMessage;
            sFg = _statusFg;
            sBg = _statusBg;
        }
        else
        {
            _statusMessage = null;
        }

        lock (_renderLock)
        {
            if (!_running || _session.Windows.Count == 0) return;
            _renderer.Render(_session, cmdInput, sFg, sBg, cmdMode);
        }
    }

    private void RenderServerMode()
    {
        if (_renderer == null) return;

        var snapshot = _lastSnapshot;
        if (snapshot == null) return;

        bool cmdMode = _commandLine.IsActive;
        string? cmdInput = cmdMode ? _commandLine.Input : null;
        string? statusOverlay = null;
        var sFg = ConsoleColor.Black;
        var sBg = ConsoleColor.Green;

        if (!cmdMode && _inputHandler != null && _inputHandler.IsSelectionMode)
        {
            statusOverlay = "selection mode";
            sFg = ConsoleColor.Black;
            sBg = ConsoleColor.Yellow;
        }
        else if (!cmdMode && _inputHandler != null && (_inputHandler.IsPrefixActive || _inputHandler.IsPrefixPending))
        {
            statusOverlay = _inputHandler.PrefixProgress;
            sFg = ConsoleColor.White;
            sBg = ConsoleColor.DarkMagenta;
        }
        else if (!cmdMode && _statusMessage != null && DateTime.Now < _statusExpiry)
        {
            statusOverlay = _statusMessage;
            sFg = _statusFg;
            sBg = _statusBg;
        }
        else if (!cmdMode)
        {
            _statusMessage = null;
        }

        lock (_renderLock)
        {
            _renderer.RenderSnapshot(snapshot, cmdInput, statusOverlay, sFg, sBg, cmdMode);
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

                if (_serverMode && _stream != null)
                {
                    SendToServer(new ResizeMessage { Width = w, Height = h });
                }

                _needsRender = true;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Standalone mode helpers
    // ─────────────────────────────────────────────────────────────

    private void CloseActivePane()
    {
        if (_session == null) return;

        var window = _session.ActiveWindow;
        var pane = window.ActivePane;
        var panes = window.GetPanes();

        if (panes.Count > 1)
        {
            lock (_renderLock) { window.ClosePane(pane); }
            WireOutputEvents();
            _needsRender = true;
        }
        else if (_session.Windows.Count > 1)
        {
            lock (_renderLock) { _session.CloseWindow(window); }
            _needsRender = true;
        }
        else
        {
            _running = false;
        }
    }

    private readonly HashSet<int> _wiredPaneIds = new();

    private void WireOutputEvents()
    {
        if (_session == null) return;
        foreach (var win in _session.Windows)
        {
            foreach (var pane in win.GetPanes())
            {
                if (_wiredPaneIds.Add(pane.Id))
                {
                    pane.OutputReceived += _ => _needsRender = true;
                    pane.ProcessExited += OnPaneExited;
                }
            }
        }
    }

    private void OnPaneExited(Pane exitedPane)
    {
        // Schedule on a thread-pool thread to avoid deadlocking the read thread
        ThreadPool.QueueUserWorkItem(_ => HandlePaneExit(exitedPane));
    }

    private void HandlePaneExit(Pane exitedPane)
    {
        if (_session == null) return;

        Window? ownerWindow = null;
        foreach (var win in _session.Windows)
        {
            if (win.GetPanes().Contains(exitedPane))
            {
                ownerWindow = win;
                break;
            }
        }

        if (ownerWindow == null) return;

        var panes = ownerWindow.GetPanes();
        if (panes.Count > 1)
        {
            lock (_renderLock) { ownerWindow.ClosePane(exitedPane); }
            _needsRender = true;
        }
        else if (_session.Windows.Count > 1)
        {
            lock (_renderLock) { _session.CloseWindow(ownerWindow); }
            _needsRender = true;
        }
        else
        {
            _running = false;
        }
    }
}
