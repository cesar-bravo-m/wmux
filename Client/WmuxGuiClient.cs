using System.IO.Pipes;
using System.Windows.Forms;
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
/// GUI-based wmux client. Opens its own Win32 window that acts as a
/// terminal emulator, capturing keyboard input directly via WM_KEYDOWN
/// and rendering the character grid using GDI.  This completely avoids
/// the console subsystem, so Ctrl+B and all other control keys are
/// received cleanly without echoing or interception by PowerShell/cmd.
///
/// Two modes:
///   1. Standalone: owns a local Session with ConPTY processes (no server).
///   2. Server (thin client): receives ScreenSnapshotMessages from the server,
///      sends input/commands back. No local Session.
/// </summary>
public class WmuxGuiClient
{
    private readonly WmuxConfig _config;
    private readonly KeyBindings _keys;
    private readonly CommandLine _commandLine = new();
    private readonly CommandRegistry _commands = new();
    private GuiRenderer? _renderer;
    private InputHandler? _inputHandler;
    private Session? _session;
    private TerminalWindow? _termWindow;
    private volatile bool _running;
    private volatile bool _needsRender;
    private readonly object _renderLock = new();
    private string? _statusMessage;
    private DateTime _statusExpiry;
    private ConsoleColor _statusFg = ConsoleColor.Black;
    private ConsoleColor _statusBg = ConsoleColor.Green;

    // Client-server mode
    private NamedPipeClientStream? _pipe;
    private readonly object _pipeLock = new();
    private bool _serverMode;
    private ScreenSnapshotMessage? _lastSnapshot;

    public WmuxGuiClient(WmuxConfig config)
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

        _termWindow = new TerminalWindow("wmux");
        int cols = _termWindow.Cols;
        int rows = _termWindow.Rows;

        sessionName ??= "main";
        _session = new Session(sessionName, cols, rows - 1);
        _renderer = new GuiRenderer(cols, rows);
        _inputHandler = new InputHandler(_keys);

        _inputHandler.RequestExit += () => RequestClose();
        _inputHandler.RequestDetach += () => RequestClose();
        _inputHandler.RequestClosePane += CloseActivePane;
        _inputHandler.StatusMessage += (msg, fg, bg) =>
        {
            _statusMessage = msg;
            _statusFg = fg;
            _statusBg = bg;
            _statusExpiry = DateTime.Now.AddSeconds(3);
            _needsRender = true;
        };

        WireOutputEvents();
        WireTerminalEvents();

        _needsRender = true;

        var renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "GuiRenderer" };
        renderThread.Start();

        Application.Run(_termWindow);

        _running = false;
        renderThread.Join(500);
        _session?.Dispose();
    }

    // ─────────────────────────────────────────────────────────────
    //  Server mode (thin client)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Connect to a running server and attach to a session.
    /// In this mode the client owns NO Session — it receives pre-composed
    /// screen grids from the server and sends input/commands back.
    /// </summary>
    public void AttachToServer(string? sessionName = null, ClientMode mode = ClientMode.CreateOrAttach)
    {
        _serverMode = true;

        // Create and show the window FIRST so the message pump starts immediately.
        // This prevents Windows from marking the app as "not responding".
        _termWindow = new TerminalWindow("wmux");

        _inputHandler = new InputHandler(_keys);
        _inputHandler.RequestDetach += () =>
        {
            SendToServer(new DetachMessage());
            RequestClose();
        };
        _inputHandler.RequestExit += () => RequestClose();

        WireTerminalEvents();

        // NO local Session — the server owns all state.
        _session = null;
        _renderer = null;

        // Do the blocking pipe connect on a background thread AFTER the message pump starts.
        _termWindow.Shown += (_, _) =>
        {
            var connectThread = new Thread(() => ConnectToServerBackground(sessionName, mode))
            {
                IsBackground = true,
                Name = "ServerConnect"
            };
            connectThread.Start();
        };

        Application.Run(_termWindow);

        // Cleanup
        _running = false;
        _pipe?.Dispose();
    }

    /// <summary>
    /// Background thread: connect to the server pipe and start the receive loop.
    /// Posts errors back to the UI thread.
    /// </summary>
    private void ConnectToServerBackground(string? sessionName, ClientMode mode)
    {
        _pipe = new NamedPipeClientStream(".", WmuxServer.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            _pipe.Connect(5000);
        }
        catch
        {
            RequestClose();
            return;
        }

        int cols = _termWindow!.Cols;
        int rows = _termWindow.Rows;

        switch (mode)
        {
            case ClientMode.ForceCreate:
                SendToServer(new NewSessionMessage
                {
                    Name = sessionName ?? "",
                    Width = cols,
                    Height = rows,
                    ForceCreate = true,
                });
                break;

            case ClientMode.Attach:
                SendToServer(new AttachMessage { SessionName = sessionName });
                SendToServer(new ResizeMessage { Width = cols, Height = rows });
                break;

            case ClientMode.CreateOrAttach:
            default:
                SendToServer(new NewSessionMessage
                {
                    Name = sessionName ?? "0",
                    Width = cols,
                    Height = rows,
                    ForceCreate = false,
                });
                break;
        }

        // Start message receive loop (replaces render loop in server mode)
        ReceiveLoop();
    }

    /// <summary>
    /// Thread-safe send to the server pipe.
    /// </summary>
    private void SendToServer(IpcMessage message)
    {
        if (_pipe == null || !_pipe.IsConnected) return;
        // Must not do synchronous pipe I/O on the UI thread — it blocks the
        // WinForms message pump and freezes the window.  Fire-and-forget on
        // the thread pool; _pipeLock serializes concurrent writes.
        _ = Task.Run(() =>
        {
            try
            {
                lock (_pipeLock)
                {
                    if (_pipe != null && _pipe.IsConnected)
                        IpcProtocol.Send(_pipe, message);
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        });
    }

    /// <summary>
    /// Receive loop for server mode. Reads messages from the pipe and
    /// dispatches them (screen snapshots, command results, errors, etc.).
    /// </summary>
    private void ReceiveLoop()
    {
        _running = true;
        while (_running && _pipe != null && _pipe.IsConnected)
        {
            try
            {
                var msg = IpcProtocol.Receive(_pipe);
                if (msg == null) break;

                switch (msg)
                {
                    case ScreenSnapshotMessage snapshot:
                        ApplySnapshot(snapshot);
                        break;
                    case CommandResultMessage cr:
                        if (cr.Result != null)
                        {
                            _statusMessage = cr.Result;
                            _statusExpiry = DateTime.Now.AddSeconds(3);
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
                        }
                        break;
                    case ErrorMessage err:
                        _statusMessage = err.Text;
                        _statusExpiry = DateTime.Now.AddSeconds(5);
                        break;
                    case AttachMessage am:
                        _termWindow?.SetTitle($"wmux - {am.SessionName}");
                        break;
                    case SessionClosedMessage:
                        RequestClose();
                        break;
                }
            }
            catch (IOException) { break; }
            catch (ObjectDisposedException) { break; }
        }

        // Server disconnected — close window
        if (_running)
            RequestClose();
    }

    /// <summary>
    /// Unpack a ScreenSnapshotMessage and push the grid to the TerminalWindow.
    /// </summary>
    private void ApplySnapshot(ScreenSnapshotMessage snapshot)
    {
        if (_termWindow == null) return;
        _lastSnapshot = snapshot;

        int w = snapshot.Width;
        int h = snapshot.Height;

        var chars = new char[h, w];
        var fg = new ConsoleColor[h, w];
        var bg = new ConsoleColor[h, w];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                chars[y, x] = idx < snapshot.Chars.Length ? snapshot.Chars[idx] : ' ';
                fg[y, x] = idx < snapshot.Fg.Length ? (ConsoleColor)snapshot.Fg[idx] : ConsoleColor.Gray;
                bg[y, x] = idx < snapshot.Bg.Length ? (ConsoleColor)snapshot.Bg[idx] : ConsoleColor.Black;
            }
        }

        // Overlay command-line prompt if active (this is client-local state)
        bool cmdMode = _commandLine.IsActive;
        if (cmdMode)
        {
            string cmdLine = $":{_commandLine.Input}";
            int statusRow = h - 1;
            if (statusRow >= 0)
            {
                // Yellow background for the entire status bar
                for (int x = 0; x < w; x++)
                {
                    chars[statusRow, x] = x < cmdLine.Length ? cmdLine[x] : ' ';
                    fg[statusRow, x] = ConsoleColor.Black;
                    bg[statusRow, x] = ConsoleColor.Yellow;
                }
                // Non-blinking black block cursor after the typed text
                int cursorX = cmdLine.Length;
                if (cursorX < w)
                {
                    bg[statusRow, cursorX] = ConsoleColor.Black;
                    fg[statusRow, cursorX] = ConsoleColor.Yellow;
                }
            }
        }
        else if (_statusMessage != null && DateTime.Now < _statusExpiry)
        {
            int statusRow = h - 1;
            if (statusRow >= 0)
            {
                for (int x = 0; x < Math.Min(_statusMessage.Length, w); x++)
                {
                    chars[statusRow, x] = _statusMessage[x];
                    fg[statusRow, x] = _statusFg;
                    bg[statusRow, x] = _statusBg;
                }
            }
        }

        // In command mode, hide the regular blinking cursor
        _termWindow.UpdateGrid(chars, fg, bg, snapshot.CursorRow, snapshot.CursorCol,
            cmdMode ? false : snapshot.CursorVisible);
    }

    // ─────────────────────────────────────────────────────────────
    //  Shared (both modes)
    // ─────────────────────────────────────────────────────────────

    private void WireTerminalEvents()
    {
        if (_termWindow == null) return;

        _termWindow.KeyPressed += (key) =>
        {
            ProcessInput(key);
        };

        _termWindow.TerminalResized += (cols, rows) =>
        {
            if (_serverMode)
            {
                SendToServer(new ResizeMessage { Width = cols, Height = rows });
            }
            else
            {
                lock (_renderLock)
                {
                    _session?.Resize(cols, rows - 1);
                    _renderer?.Resize(cols, rows);
                }
                _needsRender = true;
            }
        };

        _termWindow.WindowClosed += () =>
        {
            _running = false;
            if (_serverMode)
                SendToServer(new DetachMessage());
        };
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

        if (action != null)
            SendToServer(new CommandMessage { Command = action });
        else if (!consumed)
        {
            var vtSeq = InputHandler.KeyToVtSequence(key);
            if (vtSeq.Length > 0)
                SendToServer(new InputMessage { Data = vtSeq });
        }

        // Command mode is client-local state — the server doesn't send new
        // snapshots for it. Re-apply the last cached snapshot so the command
        // line overlay (yellow bar, cursor, typed text) is updated immediately.
        if (consumed && _lastSnapshot != null)
            ApplySnapshot(_lastSnapshot);
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
                _statusExpiry = DateTime.Now.AddSeconds(3);
                _statusFg = ConsoleColor.Black;
                _statusBg = ConsoleColor.Green;
            }
            WireOutputEvents();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Standalone mode helpers
    // ─────────────────────────────────────────────────────────────

    private void RenderLoop()
    {
        _running = true;
        while (_running)
        {
            if (_needsRender && _session != null && _renderer != null && _termWindow != null)
            {
                _needsRender = false;
                try
                {
                    if (_session.Windows.Count == 0) continue;

                    bool cmdMode = _commandLine.IsActive;
                    string? cmdInput = cmdMode ? _commandLine.Input : null;
                    var sFg = ConsoleColor.Black;
                    var sBg = ConsoleColor.Green;
                    if (cmdMode)
                    {
                        sFg = ConsoleColor.Black;
                        sBg = ConsoleColor.Yellow;
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
                        if (!_running || _session.Windows.Count == 0) continue;
                        _renderer.Render(_session, _termWindow, cmdInput, sFg, sBg, cmdMode);
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (InvalidOperationException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Render error: {ex.Message}");
                }
            }
            Thread.Sleep(16);
        }
    }

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
            _termWindow?.Close();
        }
    }

    private void RequestClose()
    {
        _running = false;
        if (_termWindow != null && _termWindow.IsHandleCreated && !_termWindow.IsDisposed)
        {
            try
            {
                _termWindow.BeginInvoke(() =>
                {
                    if (!_termWindow.IsDisposed)
                        _termWindow.Close();
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
        else
        {
            // Form not ready or already disposed — force the message loop to exit
            try { Application.ExitThread(); } catch { }
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
        if (_session == null || _termWindow == null) return;

        if (_termWindow.IsHandleCreated && !_termWindow.IsDisposed)
        {
            try
            {
                _termWindow.BeginInvoke(() => HandlePaneExit(exitedPane));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
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
            _termWindow?.Close();
        }
    }
}
