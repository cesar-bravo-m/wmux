using Wmux.Config;
using Wmux.Core;
using Wmux.Terminal;
using Wmux.UI;

namespace Wmux.Client;

/// <summary>
/// Handles keyboard input with a configurable activation string.
/// Typing the activation string (default "za") activates prefix mode;
/// the next key is a command. If only a partial match occurs, the
/// already-typed characters are flushed to DeferredKeys and forwarded
/// to the shell.
/// </summary>
public class InputHandler
{
    private enum PrefixState { Idle, Pending, Active }

    /// <summary>
    /// How long to wait (in ms) after the activation string is partially or fully
    /// typed before flushing it as literal characters. 1 second strikes a balance
    /// between comfortable command entry and responsive text input.
    /// </summary>
    public const int PrefixTimeoutMs = 1000;

    private readonly KeyBindings _keys;
    private PrefixState _state = PrefixState.Idle;
    private int _activationIndex;
    private readonly List<ConsoleKeyInfo> _pendingKeys = new();
    private bool _renameMode;
    private string _renameBuffer = "";
    private string _lineBuffer = "";

    // Selection mode routing flag (actual cursor/scroll state lives on Pane)
    private bool _selectionMode;

    public event Action? RequestDetach;
    public event Action? RequestExit;
    public event Action<string, ConsoleColor, ConsoleColor>? StatusMessage;

    /// <summary>
    /// Fired when the user presses Ctrl+D to close the active pane.
    /// On Windows, Ctrl+D is not interpreted as EOF by PowerShell/cmd,
    /// so wmux intercepts it and closes the pane directly.
    /// </summary>
    public event Action? RequestClosePane;

    /// <summary>
    /// Keys that must be forwarded to the shell before the current key.
    /// Populated when pending activation characters are flushed because
    /// the sequence didn't complete. The caller must forward these
    /// BEFORE the current key (if not consumed).
    /// Cleared at the start of each HandleKey / HandleKeyServerMode call.
    /// </summary>
    public List<ConsoleKeyInfo> DeferredKeys { get; } = new();

    /// <summary>
    /// True when part of the activation string has been typed.
    /// </summary>
    public bool IsPrefixPending => _state == PrefixState.Pending;

    /// <summary>
    /// True when the full activation string has been typed and we're
    /// waiting for the command key.
    /// </summary>
    public bool IsPrefixActive => _state == PrefixState.Active;

    /// <summary>
    /// The portion of the activation string typed so far (for status bar display).
    /// Returns the full activation string + " -" when prefix is active.
    /// </summary>
    public string PrefixProgress
    {
        get
        {
            if (_state == PrefixState.Active)
                return _keys.ActivationString + " -";
            if (_state == PrefixState.Pending)
                return _keys.ActivationString[.._activationIndex];
            return "";
        }
    }

    /// <summary>
    /// True when the activation string is partially or fully typed and we're
    /// still waiting for more input. Used to decide whether ReadKey should
    /// use a timeout.
    /// </summary>
    public bool HasPendingPrefix => _state != PrefixState.Idle;

    /// <summary>True when the pane is in selection (copy) mode.</summary>
    public bool IsSelectionMode => _selectionMode;

    /// <summary>
    /// Flush the pending/active prefix state as if the user never intended
    /// to issue a command. Returns the literal characters to forward to the
    /// shell, or null if there's nothing to flush.
    /// </summary>
    public string? FlushPrefixTimeout()
    {
        if (_state == PrefixState.Idle) return null;

        string flushed;
        if (_state == PrefixState.Pending)
        {
            // Build the string from the pending ConsoleKeyInfo list
            var chars = new char[_pendingKeys.Count];
            int n = 0;
            foreach (var k in _pendingKeys)
                if (k.KeyChar != '\0')
                    chars[n++] = k.KeyChar;
            flushed = new string(chars, 0, n);
        }
        else // Active — the full activation string was matched, but _pendingKeys was cleared
        {
            flushed = _keys.ActivationString;
        }

        // Update _lineBuffer so CheckExitCommand tracking stays in sync
        foreach (char c in flushed)
            if (c >= ' ')
                _lineBuffer += c;

        _state = PrefixState.Idle;
        _activationIndex = 0;
        _pendingKeys.Clear();

        return flushed.Length > 0 ? flushed : null;
    }

    /// <summary>
    /// Enter selection mode. Called by WmuxClient in server mode after
    /// receiving the selection-enter command acknowledgement.
    /// </summary>
    public void EnterSelectionMode()
    {
        _selectionMode = true;
    }

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
        DeferredKeys.Clear();

        // Command mode takes priority
        if (commandLine.IsActive)
        {
            command = commandLine.HandleKey(key);
            return true;
        }

        // Selection mode — intercept all keys for navigation
        if (_selectionMode)
        {
            HandleSelectionModeKeyLocal(key, session.ActiveWindow.ActivePane);
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

        // Activation string prefix sequence
        if (_state != PrefixState.Active)
        {
            if (ProcessPrefixSequence(key))
                return true;
            // If not consumed, DeferredKeys may contain flushed characters.
            // The current key continues through normal processing below.
        }

        // Ctrl+D — always close the active pane.
        // On Windows, PowerShell/cmd don't use Ctrl+D as EOF,
        // so wmux intercepts it unconditionally.
        if (key.Key == ConsoleKey.D && key.Modifiers == ConsoleModifiers.Control)
        {
            RequestClosePane?.Invoke();
            return true;
        }

        // Prefix is active — handle bound keys
        if (_state == PrefixState.Active)
        {
            _state = PrefixState.Idle;
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
            if (ch == _keys.CopyMode)
            {
                window.ActivePane.EnterSelectionMode();
                _selectionMode = true;
                return true;
            }
            if (ch == _keys.BreakPane)
            {
                if (!session.BreakPane())
                    StatusMessage?.Invoke("Cannot break: only one pane", ConsoleColor.Black, ConsoleColor.Yellow);
                return true;
            }
            return true; // Consume unknown prefix keys
        }

        // Not in prefix mode — normal key handling
        if (CheckExitCommand(key))
        {
            RequestClosePane?.Invoke();
            return true;
        }
        return false; // Forward to active pane
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
        DeferredKeys.Clear();

        // Command mode takes priority
        if (commandLine.IsActive)
        {
            action = commandLine.HandleKey(key);
            return true;
        }

        // Selection mode — intercept all keys, send commands to server
        if (_selectionMode)
        {
            HandleSelectionModeKeyServer(key, out action);
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

        // Activation string prefix sequence
        if (_state != PrefixState.Active)
        {
            if (ProcessPrefixSequence(key))
                return true;
        }

        // Ctrl+D — always close the active pane.
        if (key.Key == ConsoleKey.D && key.Modifiers == ConsoleModifiers.Control)
        {
            action = "kill-pane";
            return true;
        }

        // Prefix is active — handle bound keys
        if (_state == PrefixState.Active)
        {
            _state = PrefixState.Idle;

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
            if (ch == _keys.CopyMode)
            {
                action = "selection-enter";
                _selectionMode = true;
                return true;
            }
            if (ch == _keys.BreakPane) { action = "break-pane"; return true; }
            return true; // Consume unknown prefix keys
        }

        // Not in prefix mode — normal key handling
        if (CheckExitCommand(key))
        {
            action = "kill-pane";
            return true;
        }
        return false; // Forward to active pane
    }

    /// <summary>
    /// Handle selection mode key in standalone mode — directly modifies pane state.
    /// </summary>
    private void HandleSelectionModeKeyLocal(ConsoleKeyInfo key, Pane pane)
    {
        if (key.Key == ConsoleKey.Escape || key.KeyChar == 'q')
        {
            _selectionMode = false;
            pane.ExitSelectionMode();
            return;
        }

        // SPACE toggles highlight start / copies selection
        if (key.Key == ConsoleKey.Spacebar)
        {
            if (!pane.SelectionHighlightActive)
            {
                pane.StartSelectionHighlight();
            }
            else
            {
                string text = pane.ExtractSelectedText();
                ClipboardHelper.SetText(text);
                _selectionMode = false;
                pane.ExitSelectionMode();
                StatusMessage?.Invoke("selection copied to clipboard",
                    ConsoleColor.Black, ConsoleColor.Green);
            }
            return;
        }

        if (key.Key == ConsoleKey.UpArrow || key.KeyChar == 'k') { pane.SelectionMoveUp(); return; }
        if (key.Key == ConsoleKey.DownArrow || key.KeyChar == 'j') { pane.SelectionMoveDown(); return; }
        if (key.Key == ConsoleKey.LeftArrow || key.KeyChar == 'h') { pane.SelectionMoveLeft(); return; }
        if (key.Key == ConsoleKey.RightArrow || key.KeyChar == 'l') { pane.SelectionMoveRight(); return; }
        // All other keys consumed silently
    }

    /// <summary>
    /// Handle selection mode key in server mode — returns command strings.
    /// </summary>
    private void HandleSelectionModeKeyServer(ConsoleKeyInfo key, out string? action)
    {
        action = null;

        if (key.Key == ConsoleKey.Escape || key.KeyChar == 'q')
        {
            _selectionMode = false;
            action = "selection-exit";
            return;
        }

        // SPACE toggles highlight start / copies selection (server decides which)
        if (key.Key == ConsoleKey.Spacebar)
        {
            action = "selection-toggle";
            return;
        }

        if (key.Key == ConsoleKey.UpArrow || key.KeyChar == 'k') { action = "selection-move -U"; return; }
        if (key.Key == ConsoleKey.DownArrow || key.KeyChar == 'j') { action = "selection-move -D"; return; }
        if (key.Key == ConsoleKey.LeftArrow || key.KeyChar == 'h') { action = "selection-move -L"; return; }
        if (key.Key == ConsoleKey.RightArrow || key.KeyChar == 'l') { action = "selection-move -R"; return; }
        // All other keys consumed silently
    }

    /// <summary>
    /// Called externally (e.g. by client on receiving server copy acknowledgement)
    /// to reset the local selection mode flag.
    /// </summary>
    public void ExitSelectionModeExternal()
    {
        _selectionMode = false;
    }

    /// <summary>
    /// Process the activation string character by character.
    /// Returns true if the key was consumed by prefix logic.
    /// When pending characters are flushed (sequence didn't complete),
    /// they're added to DeferredKeys and the current key continues processing.
    /// </summary>
    private bool ProcessPrefixSequence(ConsoleKeyInfo key)
    {
        string act = _keys.ActivationString;

        if (_state == PrefixState.Pending)
        {
            // We're partway through matching the activation string
            if (key.KeyChar == act[_activationIndex] && key.Modifiers == 0)
            {
                _pendingKeys.Add(key);
                _activationIndex++;
                if (_activationIndex >= act.Length)
                {
                    // Full match — activate prefix
                    _state = PrefixState.Active;
                    _activationIndex = 0;
                    _pendingKeys.Clear();
                    return true;
                }
                return true;
            }

            // Check if this key could restart the activation sequence
            if (key.KeyChar == act[0] && key.Modifiers == 0)
            {
                // Flush all pending, start a new sequence with this key
                FlushPendingKeys();
                _activationIndex = 1;
                _pendingKeys.Add(key);
                return true;
            }

            // No match — flush all pending, current key continues normally
            FlushPendingKeys();
            _state = PrefixState.Idle;
            _activationIndex = 0;
            return false;
        }

        // Idle — check for first character of activation string
        if (key.KeyChar == act[0] && key.Modifiers == 0)
        {
            _activationIndex = 1;
            _pendingKeys.Add(key);
            _state = PrefixState.Pending;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Move all pending keys into DeferredKeys and update _lineBuffer.
    /// </summary>
    private void FlushPendingKeys()
    {
        foreach (var pending in _pendingKeys)
        {
            DeferredKeys.Add(pending);
            if (pending.KeyChar >= ' ')
                _lineBuffer += pending.KeyChar;
        }
        _pendingKeys.Clear();
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
