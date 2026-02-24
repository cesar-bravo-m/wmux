using Wmux.Core;

namespace Wmux.Commands;

/// <summary>
/// Registry of all available : commands, similar to tmux command mode.
/// </summary>
public class CommandRegistry
{
    public string? Execute(ParsedCommand cmd, Session session)
    {
        return cmd.Name switch
        {
            "split-window" or "splitw" => SplitWindow(cmd, session),
            "new-window" or "neww" => NewWindow(cmd, session),
            "kill-pane" or "killp" => KillPane(session),
            "kill-window" or "killw" => KillWindow(session),
            "select-window" or "selectw" => SelectWindow(cmd, session),
            "rename-window" or "renamew" => RenameWindow(cmd, session),
            "select-pane" or "selectp" => SelectPane(cmd, session),
            "resize-pane" or "resizep" => ResizePane(cmd, session),
            "list-windows" or "lsw" => ListWindows(session),
            "list-panes" or "lsp" => ListPanes(session),
            "select-layout" => SelectLayout(cmd, session),
            "swap-pane" => SwapPane(cmd, session),
            "next-window" or "nextw" => NextWindow(session),
            "prev-window" or "prevw" => PrevWindow(session),
            "break-pane" or "breakp" => BreakPane(session),
            "next-pane" or "nextp" => NextPane(session),
            "selection-enter" => SelectionEnter(session),
            "selection-exit" => SelectionExit(session),
            "selection-move" => SelectionMove(cmd, session),
            "selection-toggle" => SelectionToggle(session),
            _ => $"Unknown command: {cmd.Name}"
        };
    }

    private string? SplitWindow(ParsedCommand cmd, Session session)
    {
        var dir = SplitDirection.Horizontal;
        foreach (var arg in cmd.Args)
        {
            if (arg == "-h") dir = SplitDirection.Horizontal;
            if (arg == "-v") dir = SplitDirection.Vertical;
        }
        var result = session.ActiveWindow.SplitPane(dir);
        return result == null ? "Cannot split: pane too small" : null;
    }

    private string? NewWindow(ParsedCommand cmd, Session session)
    {
        var name = cmd.Args.Length > 0 ? string.Join(" ", cmd.Args) : null;
        var win = session.CreateWindow(session.ActiveWindow.Width, session.ActiveWindow.Height);
        if (name != null) win.Name = name;
        return null;
    }

    /// <summary>
    /// Returns "destroy-session" when the kill-pane/kill-window command should
    /// destroy the entire session (last pane in last window). The server handles
    /// this specially because Dispose can deadlock if called under the server's lock.
    /// </summary>
    private string? KillPane(Session session)
    {
        var win = session.ActiveWindow;
        var panes = win.GetPanes();
        if (panes.Count > 1)
        {
            win.ClosePane(win.ActivePane);
        }
        else if (session.Windows.Count > 1)
        {
            session.CloseWindow(win);
        }
        else
        {
            // Signal the server to destroy this session (handled outside lock)
            return "\x01destroy-session";
        }
        return null;
    }

    private string? KillWindow(Session session)
    {
        if (session.Windows.Count > 1)
        {
            session.CloseWindow(session.ActiveWindow);
        }
        else
        {
            return "\x01destroy-session";
        }
        return null;
    }

    private string? SelectWindow(ParsedCommand cmd, Session session)
    {
        if (cmd.Args.Length > 0 && int.TryParse(cmd.Args[0], out int idx))
            session.SelectWindow(idx);
        return null;
    }

    private string? RenameWindow(ParsedCommand cmd, Session session)
    {
        if (cmd.Args.Length > 0)
            session.ActiveWindow.Name = string.Join(" ", cmd.Args);
        return null;
    }

    private string? SelectPane(ParsedCommand cmd, Session session)
    {
        foreach (var arg in cmd.Args)
        {
            switch (arg)
            {
                case "-U": session.ActiveWindow.NavigatePane(ConsoleKey.UpArrow); break;
                case "-D": session.ActiveWindow.NavigatePane(ConsoleKey.DownArrow); break;
                case "-L": session.ActiveWindow.NavigatePane(ConsoleKey.LeftArrow); break;
                case "-R": session.ActiveWindow.NavigatePane(ConsoleKey.RightArrow); break;
            }
        }
        return null;
    }

    private string? ResizePane(ParsedCommand cmd, Session session)
    {
        // Simplified resize - recalculate layout
        session.ActiveWindow.Resize(session.ActiveWindow.Width, session.ActiveWindow.Height);
        return null;
    }

    private string? ListWindows(Session session)
    {
        var lines = session.Windows.Select((w, i) =>
            $"{i}: {w.Name} ({w.GetPanes().Count} panes){(w == session.ActiveWindow ? " (active)" : "")}");
        return string.Join("\n", lines);
    }

    private string? ListPanes(Session session)
    {
        var panes = session.ActiveWindow.GetPanes();
        var lines = panes.Select((p, i) =>
            $"{i}: [{p.Width}x{p.Height}] at ({p.Left},{p.Top}){(p == session.ActiveWindow.ActivePane ? " (active)" : "")}");
        return string.Join("\n", lines);
    }

    private string? BreakPane(Session session)
    {
        if (!session.BreakPane())
            return "Cannot break: only one pane";
        return null;
    }

    private string? NextWindow(Session session)
    {
        if (session.Windows.Count <= 1)
            return "No next window";
        session.NextWindow();
        return null;
    }

    private string? PrevWindow(Session session)
    {
        if (session.Windows.Count <= 1)
            return "No previous window";
        session.PrevWindow();
        return null;
    }

    private string? NextPane(Session session)
    {
        session.ActiveWindow.NextPane();
        return null;
    }

    private string? SelectLayout(ParsedCommand cmd, Session session)
    {
        if (cmd.Args.Length == 0) return "Usage: select-layout <layout-name>";

        var arg = cmd.Args[0].ToLowerInvariant();

        if (arg == "cycle")
        {
            session.ActiveWindow.CycleLayout();
            return null;
        }

        LayoutPreset preset = arg switch
        {
            "even-horizontal" => LayoutPreset.EvenHorizontal,
            "even-vertical" => LayoutPreset.EvenVertical,
            "main-horizontal" => LayoutPreset.MainHorizontal,
            "main-vertical" => LayoutPreset.MainVertical,
            "tiled" => LayoutPreset.Tiled,
            _ => LayoutPreset.Tiled
        };

        var win = session.ActiveWindow;
        win.Layout.ApplyPreset(preset, 0, 0, win.Width, win.Height);
        return null;
    }

    private string? SwapPane(ParsedCommand cmd, Session session)
    {
        foreach (var arg in cmd.Args)
        {
            switch (arg)
            {
                case "-U": session.ActiveWindow.NavigatePane(ConsoleKey.UpArrow); break;
                case "-D": session.ActiveWindow.NavigatePane(ConsoleKey.DownArrow); break;
            }
        }
        return null;
    }

    private string? SelectionEnter(Session session)
    {
        session.ActiveWindow.ActivePane.EnterSelectionMode();
        return null;
    }

    private string? SelectionExit(Session session)
    {
        session.ActiveWindow.ActivePane.ExitSelectionMode();
        return null;
    }

    private string? SelectionMove(ParsedCommand cmd, Session session)
    {
        var pane = session.ActiveWindow.ActivePane;
        foreach (var arg in cmd.Args)
        {
            switch (arg)
            {
                case "-U": pane.SelectionMoveUp(); break;
                case "-D": pane.SelectionMoveDown(); break;
                case "-L": pane.SelectionMoveLeft(); break;
                case "-R": pane.SelectionMoveRight(); break;
            }
        }
        return null;
    }

    private string? SelectionToggle(Session session)
    {
        var pane = session.ActiveWindow.ActivePane;
        if (!pane.IsInSelectionMode) return null;

        if (!pane.SelectionHighlightActive)
        {
            pane.StartSelectionHighlight();
            return null;
        }
        else
        {
            string text = pane.ExtractSelectedText();
            Terminal.ClipboardHelper.SetText(text);
            pane.ExitSelectionMode();
            return "selection copied to clipboard";
        }
    }
}
