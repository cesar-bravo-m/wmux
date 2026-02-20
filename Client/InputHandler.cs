using Wmux.Config;
using Wmux.Core;
using Wmux.UI;

namespace Wmux.Client;

/// <summary>
/// Handles keyboard input with prefix key support (like tmux Ctrl+B).
/// </summary>
public class InputHandler
{
    private readonly KeyBindings _keys;
    private bool _prefixActive;
    private bool _renameMode;
    private string _renameBuffer = "";
    private string _lineBuffer = "";

    public event Action? RequestDetach;
    public event Action? RequestExit;
    public event Action<string, ConsoleColor, ConsoleColor>? StatusMessage;

    /// <summary>
    /// Fired when the user presses Ctrl+D to close the active pane.
    /// On Windows, Ctrl+D is not interpreted as EOF by PowerShell/cmd,
    /// so wmux intercepts it and closes the pane directly.
    /// </summary>
    public event Action? RequestClosePane;

    public InputHandler(KeyBindings keys)
    {
        _keys = keys;
    }

    /// <summary>
    /// Process a key press. Returns true if the key was consumed (not forwarded to pane).
    /// </summary>
    public bool HandleKey(ConsoleKeyInfo key, Session session, CommandLine commandLine, out string? command)
    {
        command = null;

        // Command mode takes priority
        if (commandLine.IsActive)
        {
            command = commandLine.HandleKey(key);
            return true;
        }

        // Rename mode
        if (_renameMode)
        {
            if (key.Key == ConsoleKey.Enter)
            {
                _renameMode = false;
                return true;
            }
            if (key.Key == ConsoleKey.Escape)
            {
                _renameMode = false;
                return true;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (_renameBuffer.Length > 0)
                {
                    _renameBuffer = _renameBuffer[..^1];
                    session.ActiveWindow.Name = _renameBuffer;
                }
                return true;
            }
            if (key.KeyChar >= ' ')
            {
                _renameBuffer += key.KeyChar;
                session.ActiveWindow.Name = _renameBuffer;
            }
            return true;
        }

        // Ctrl+D — close the active pane only if the input line is empty
        if (key.Key == ConsoleKey.D && key.Modifiers == ConsoleModifiers.Control)
        {
            if (_lineBuffer.Length == 0)
            {
                RequestClosePane?.Invoke();
                return true;
            }
            return false; // Forward Ctrl+D to the shell (shows ^D)
        }

        // Check for prefix key
        if (_keys.IsPrefixKey(key))
        {
            _prefixActive = true;
            return true;
        }

        if (!_prefixActive)
        {
            if (CheckExitCommand(key))
            {
                RequestClosePane?.Invoke();
                return true;
            }
            return false; // Forward to active pane
        }

        // Prefix is active - handle bound keys
        _prefixActive = false;
        var window = session.ActiveWindow;

        // Window selection by number
        if (key.KeyChar >= '0' && key.KeyChar <= '9')
        {
            session.SelectWindow(key.KeyChar - '0');
            return true;
        }

        // Arrow key pane navigation
        if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.LeftArrow or ConsoleKey.RightArrow)
        {
            window.NavigatePane(key.Key);
            return true;
        }

        // hjkl pane navigation
        if (key.KeyChar == 'h') { window.NavigatePane(ConsoleKey.LeftArrow); return true; }
        if (key.KeyChar == 'j') { window.NavigatePane(ConsoleKey.DownArrow); return true; }
        if (key.KeyChar == 'k') { window.NavigatePane(ConsoleKey.UpArrow); return true; }
        if (key.KeyChar == 'l') { window.NavigatePane(ConsoleKey.RightArrow); return true; }

        char ch = key.KeyChar;

        if (ch == _keys.SplitHorizontal || char.ToLower(ch) == char.ToLower(_keys.SplitHorizontal))
        {
            window.SplitPane(SplitDirection.Horizontal);
            return true;
        }
        if (ch == _keys.SplitVertical || ch == 'v')
        {
            window.SplitPane(SplitDirection.Vertical);
            return true;
        }
        if (ch == _keys.NewWindow)
        {
            session.CreateWindow(window.Width, window.Height);
            return true;
        }
        if (ch == _keys.NextWindow)
        {
            if (session.Windows.Count <= 1)
                StatusMessage?.Invoke("No next window", ConsoleColor.Black, ConsoleColor.Yellow);
            else
                session.NextWindow();
            return true;
        }
        if (ch == _keys.PrevWindow)
        {
            if (session.Windows.Count <= 1)
                StatusMessage?.Invoke("No previous window", ConsoleColor.Black, ConsoleColor.Yellow);
            else
                session.PrevWindow();
            return true;
        }
        if (ch == _keys.Detach)
        {
            RequestDetach?.Invoke();
            return true;
        }
        if (ch == _keys.KillPane)
        {
            var panes = window.GetPanes();
            if (panes.Count > 1)
                window.ClosePane(window.ActivePane);
            else if (session.Windows.Count > 1)
                session.CloseWindow(window);
            else
                RequestExit?.Invoke();
            return true;
        }
        if (ch == _keys.CommandMode)
        {
            commandLine.Activate();
            return true;
        }
        if (ch == _keys.RenameWindow)
        {
            _renameMode = true;
            _renameBuffer = window.Name;
            return true;
        }
        if (ch == _keys.KillWindow)
        {
            if (session.Windows.Count > 1)
                session.CloseWindow(window);
            else
                RequestExit?.Invoke();
            return true;
        }
        if (ch == _keys.NextPane)
        {
            window.NextPane();
            return true;
        }
        if (ch == _keys.CycleLayout)
        {
            window.CycleLayout();
            return true;
        }

        return true; // Consume unknown prefix keys
    }

    /// <summary>
    /// Server-mode key handling. Tracks prefix state locally and returns
    /// command strings instead of directly mutating a Session object.
    /// Returns true if the key was consumed (not forwarded to pane).
    /// The out parameter 'action' is a command string to send to the server,
    /// or null if no server command is needed.
    /// </summary>
    public bool HandleKeyServerMode(ConsoleKeyInfo key, CommandLine commandLine, out string? action)
    {
        action = null;

        // Command mode takes priority
        if (commandLine.IsActive)
        {
            action = commandLine.HandleKey(key);
            return true;
        }

        // Rename mode — buffer keystrokes locally, send command on Enter
        if (_renameMode)
        {
            if (key.Key == ConsoleKey.Enter)
            {
                _renameMode = false;
                if (_renameBuffer.Length > 0)
                    action = $"rename-window {_renameBuffer}";
                return true;
            }
            if (key.Key == ConsoleKey.Escape)
            {
                _renameMode = false;
                return true;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (_renameBuffer.Length > 0)
                    _renameBuffer = _renameBuffer[..^1];
                return true;
            }
            if (key.KeyChar >= ' ')
                _renameBuffer += key.KeyChar;
            return true;
        }

        // Ctrl+D — close the active pane only if the input line is empty
        if (key.Key == ConsoleKey.D && key.Modifiers == ConsoleModifiers.Control)
        {
            if (_lineBuffer.Length == 0)
            {
                action = "kill-pane";
                return true;
            }
            return false; // Forward Ctrl+D to the shell (shows ^D)
        }

        // Check for prefix key
        if (_keys.IsPrefixKey(key))
        {
            _prefixActive = true;
            return true;
        }

        if (!_prefixActive)
        {
            if (CheckExitCommand(key))
            {
                action = "kill-pane";
                return true;
            }
            return false; // Forward to active pane
        }

        // Prefix is active — handle bound keys
        _prefixActive = false;

        // Window selection by number
        if (key.KeyChar >= '0' && key.KeyChar <= '9')
        {
            action = $"select-window {key.KeyChar - '0'}";
            return true;
        }

        // Arrow key pane navigation
        if (key.Key == ConsoleKey.UpArrow) { action = "select-pane -U"; return true; }
        if (key.Key == ConsoleKey.DownArrow) { action = "select-pane -D"; return true; }
        if (key.Key == ConsoleKey.LeftArrow) { action = "select-pane -L"; return true; }
        if (key.Key == ConsoleKey.RightArrow) { action = "select-pane -R"; return true; }

        // hjkl pane navigation
        if (key.KeyChar == 'h') { action = "select-pane -L"; return true; }
        if (key.KeyChar == 'j') { action = "select-pane -D"; return true; }
        if (key.KeyChar == 'k') { action = "select-pane -U"; return true; }
        if (key.KeyChar == 'l') { action = "select-pane -R"; return true; }

        char ch = key.KeyChar;

        if (ch == _keys.SplitHorizontal || char.ToLower(ch) == char.ToLower(_keys.SplitHorizontal)) { action = "split-window -h"; return true; }
        if (ch == _keys.SplitVertical || ch == 'v') { action = "split-window -v"; return true; }
        if (ch == _keys.NewWindow) { action = "new-window"; return true; }
        if (ch == _keys.NextWindow) { action = "next-window"; return true; }
        if (ch == _keys.PrevWindow) { action = "prev-window"; return true; }
        if (ch == _keys.Detach)
        {
            RequestDetach?.Invoke();
            return true;
        }
        if (ch == _keys.KillPane) { action = "kill-pane"; return true; }
        if (ch == _keys.CommandMode) { commandLine.Activate(); return true; }
        if (ch == _keys.RenameWindow)
        {
            _renameMode = true;
            _renameBuffer = "";
            return true;
        }
        if (ch == _keys.KillWindow) { action = "kill-window"; return true; }
        if (ch == _keys.NextPane) { action = "next-pane"; return true; }
        if (ch == _keys.CycleLayout) { action = "select-layout cycle"; return true; }

        return true; // Consume unknown prefix keys
    }

    /// <summary>
    /// Track typed characters and detect "exit" as a wmux command.
    /// Returns true if Enter was pressed and the buffered line is "exit".
    /// </summary>
    private bool CheckExitCommand(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            bool isExit = _lineBuffer.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase);
            _lineBuffer = "";
            return isExit;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (_lineBuffer.Length > 0)
                _lineBuffer = _lineBuffer[..^1];
            return false;
        }

        // Ctrl+C or Ctrl+U clear the line
        if (key.Modifiers == ConsoleModifiers.Control &&
            key.Key is ConsoleKey.C or ConsoleKey.U)
        {
            _lineBuffer = "";
            return false;
        }

        // Arrow keys invalidate the buffer (shell history, cursor movement)
        if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow
                    or ConsoleKey.LeftArrow or ConsoleKey.RightArrow)
        {
            _lineBuffer = "";
            return false;
        }

        if (key.KeyChar >= ' ')
            _lineBuffer += key.KeyChar;

        return false;
    }

    /// <summary>
    /// Convert a key to the VT sequence to send to the child process.
    /// </summary>
    public static string KeyToVtSequence(ConsoleKeyInfo key)
    {
        // Handle Ctrl+letter combinations
        if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key >= ConsoleKey.A && key.Key <= ConsoleKey.Z)
        {
            return ((char)(key.Key - ConsoleKey.A + 1)).ToString();
        }

        return key.Key switch
        {
            ConsoleKey.Enter => "\r",
            ConsoleKey.Backspace => "\x7f",
            ConsoleKey.Tab => "\t",
            ConsoleKey.Escape => "\x1b",
            ConsoleKey.UpArrow => "\x1b[A",
            ConsoleKey.DownArrow => "\x1b[B",
            ConsoleKey.RightArrow => "\x1b[C",
            ConsoleKey.LeftArrow => "\x1b[D",
            ConsoleKey.Home => "\x1b[H",
            ConsoleKey.End => "\x1b[F",
            ConsoleKey.Insert => "\x1b[2~",
            ConsoleKey.Delete => "\x1b[3~",
            ConsoleKey.PageUp => "\x1b[5~",
            ConsoleKey.PageDown => "\x1b[6~",
            ConsoleKey.F1 => "\x1bOP",
            ConsoleKey.F2 => "\x1bOQ",
            ConsoleKey.F3 => "\x1bOR",
            ConsoleKey.F4 => "\x1bOS",
            ConsoleKey.F5 => "\x1b[15~",
            ConsoleKey.F6 => "\x1b[17~",
            ConsoleKey.F7 => "\x1b[18~",
            ConsoleKey.F8 => "\x1b[19~",
            ConsoleKey.F9 => "\x1b[20~",
            ConsoleKey.F10 => "\x1b[21~",
            ConsoleKey.F11 => "\x1b[23~",
            ConsoleKey.F12 => "\x1b[24~",
            _ => key.KeyChar != '\0' ? key.KeyChar.ToString() : ""
        };
    }
}
