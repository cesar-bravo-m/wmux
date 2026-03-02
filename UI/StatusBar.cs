using System.Text;
using Wmux.Core;

namespace Wmux.UI;

/// <summary>
/// Renders the tmux-style status bar at the bottom of the screen.
/// Green background with session and window information.
/// </summary>
public static class StatusBar
{
    public static string Render(Session session, int width, string? commandInput = null)
    {
        var sb = new StringBuilder();

        // Green background, black text
        sb.Append("\x1b[30;42m");

        if (commandInput != null)
        {
            // Command mode - show the : prompt
            var cmdLine = $":{commandInput}";
            sb.Append(cmdLine);
            int remaining = width - cmdLine.Length;
            if (remaining > 0) sb.Append(' ', remaining);
        }
        else
        {
            // Left side: [session-name] window-list
            var left = new StringBuilder();
            left.Append($"[{session.Name}] ");

            for (int i = 0; i < session.Windows.Count; i++)
            {
                var win = session.Windows[i];
                bool isActive = win == session.ActiveWindow;
                if (isActive)
                    left.Append($"{i}:{win.Name}* ");
                else
                    left.Append($"{i}:{win.Name} ");
            }

            // Right side: pane info + time
            var right = new StringBuilder();
            var activeWin = session.ActiveWindow;
            var panes = activeWin.GetPanes();
            int paneIdx = panes.IndexOf(activeWin.ActivePane) + 1;
            var paneName = activeWin.ActivePane.Name;
            if (paneName.Length > 0)
                right.Append($"[{paneIdx}/{panes.Count} \"{paneName}\"] ");
            else
                right.Append($"[{paneIdx}/{panes.Count}] ");
            right.Append(DateTime.Now.ToString("HH:mm"));

            string leftStr = left.ToString();
            string rightStr = right.ToString();

            int padding = width - leftStr.Length - rightStr.Length;
            if (padding < 0) padding = 0;

            sb.Append(leftStr);
            sb.Append(' ', padding);
            sb.Append(rightStr);
        }

        // Reset colors
        sb.Append("\x1b[0m");

        return sb.ToString();
    }

    /// <summary>
    /// Returns the status bar as plain text (no ANSI escape codes).
    /// Used by the GUI renderer which applies colours itself.
    /// </summary>
    public static string RenderPlain(Session session, int width, string? commandInput = null)
    {
        var sb = new StringBuilder();

        if (commandInput != null)
        {
            var cmdLine = $":{commandInput}";
            sb.Append(cmdLine);
            int remaining = width - cmdLine.Length;
            if (remaining > 0) sb.Append(' ', remaining);
        }
        else
        {
            var left = new StringBuilder();
            left.Append($"[{session.Name}] ");

            for (int i = 0; i < session.Windows.Count; i++)
            {
                var win = session.Windows[i];
                bool isActive = win == session.ActiveWindow;
                if (isActive)
                    left.Append($"{i}:{win.Name}* ");
                else
                    left.Append($"{i}:{win.Name} ");
            }

            var right = new StringBuilder();
            var activeWin = session.ActiveWindow;
            var panes = activeWin.GetPanes();
            int paneIdx = panes.IndexOf(activeWin.ActivePane) + 1;
            var paneName = activeWin.ActivePane.Name;
            if (paneName.Length > 0)
                right.Append($"[{paneIdx}/{panes.Count} \"{paneName}\"] ");
            else
                right.Append($"[{paneIdx}/{panes.Count}] ");
            right.Append(DateTime.Now.ToString("HH:mm"));

            string leftStr = left.ToString();
            string rightStr = right.ToString();

            int padding = width - leftStr.Length - rightStr.Length;
            if (padding < 0) padding = 0;

            sb.Append(leftStr);
            sb.Append(' ', padding);
            sb.Append(rightStr);
        }

        return sb.ToString();
    }
}
