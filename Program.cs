using System.Windows.Forms;
using Wmux.Client;
using Wmux.Config;
using Wmux.Server;

namespace Wmux;

static class Program
{
    /// <summary>
    /// CRITICAL: [STAThread] is required for WinForms. Without it, the message
    /// pump runs on an MTA thread, causing Application.Run to malfunction —
    /// the window hangs, keyboard events are not dispatched through WndProc,
    /// and control keys like Ctrl+B are passed through instead of intercepted.
    ///
    /// Top-level statements do NOT get [STAThread] injected automatically,
    /// even with UseWindowsForms in the csproj. This explicit Main is required.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        var config = WmuxConfig.Load();

        if (args.Length == 0)
        {
            // Default: start embedded server (if needed) + attach as first client
            if (!WmuxServer.IsServerRunning())
            {
                var server = new WmuxServer { EmbeddedMode = true };
                var serverTask = Task.Run(() => server.RunAsync());

                // Wait for the server to create its pipe before the client tries to connect.
                // Without this, the client races the server and pipe.Connect can timeout.
                if (!server.Ready.Wait(5000))
                {
                    // Server failed to start — check if the task faulted
                    if (serverTask.IsFaulted)
                    {
                        Console.Error.WriteLine($"wmux server failed to start: {serverTask.Exception?.InnerException?.Message}");
                    }
                    else
                    {
                        Console.Error.WriteLine("wmux server did not become ready within 5 seconds.");
                    }
                    return 1;
                }
            }

            var client = new WmuxGuiClient(config);
            client.AttachToServer(mode: ClientMode.CreateOrAttach);
            return 0;
        }

        var command = args[0].ToLowerInvariant();

        switch (command)
        {
            case "new-session" or "new" or "new-s":
            {
                string? name = null;
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i] == "-s" && i + 1 < args.Length)
                        name = args[++i];
                }

                // Start embedded server if not running
                if (!WmuxServer.IsServerRunning())
                {
                    var server = new WmuxServer { EmbeddedMode = true };
                    var serverTask = Task.Run(() => server.RunAsync());

                    if (!server.Ready.Wait(5000))
                    {
                        if (serverTask.IsFaulted)
                            Console.Error.WriteLine($"wmux server failed to start: {serverTask.Exception?.InnerException?.Message}");
                        else
                            Console.Error.WriteLine("wmux server did not become ready within 5 seconds.");
                        return 1;
                    }
                }

                var client = new WmuxGuiClient(config);
                client.AttachToServer(name, ClientMode.ForceCreate);
                return 0;
            }

            case "attach" or "attach-session" or "a" or "at":
            {
                string? name = null;
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i] == "-t" && i + 1 < args.Length)
                        name = args[++i];
                    else if (!args[i].StartsWith('-') && name == null)
                        name = args[i]; // positional: wmux attach 0
                }

                if (!WmuxServer.IsServerRunning())
                {
                    Console.Error.WriteLine("No wmux server running. Run 'wmux' first.");
                    return 1;
                }

                var client2 = new WmuxGuiClient(config);
                client2.AttachToServer(name, ClientMode.Attach);
                return 0;
            }

            case "start-server" or "server":
            {
                if (WmuxServer.IsServerRunning())
                {
                    Console.Error.WriteLine("wmux server is already running.");
                    return 1;
                }
                var server = new WmuxServer();
                // RunAsync for standalone server is fine — no WinForms here
                server.RunAsync().GetAwaiter().GetResult();
                return 0;
            }

            case "list-sessions" or "list-session" or "ls":
            {
                if (!WmuxServer.IsServerRunning())
                {
                    Console.Error.WriteLine($"no server running on \\\\.\\pipe\\{WmuxServer.PipeName}");
                    return 1;
                }
                ListSessions();
                return 0;
            }

            case "kill-server":
            {
                if (!WmuxServer.IsServerRunning())
                {
                    Console.Error.WriteLine("No wmux server running.");
                    return 1;
                }
                KillServer();
                return 0;
            }

            case "help" or "--help" or "-h":
                PrintHelp();
                return 0;

            case "version" or "--version" or "-v":
                Console.WriteLine("wmux 0.1.0");
                return 0;

            default:
                Console.Error.WriteLine($"Unknown command: {command}");
                Console.Error.WriteLine();
                PrintHelp();
                return 1;
        }
    }

    static void ListSessions()
    {
        using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", WmuxServer.PipeName, System.IO.Pipes.PipeDirection.InOut);
        pipe.Connect(3000);
        IpcProtocol.Send(pipe, new SessionInfoMessage());
        var response = IpcProtocol.Receive(pipe);
        if (response is SessionListMessage list)
        {
            if (list.Sessions.Count == 0)
            {
                Console.WriteLine("No sessions.");
            }
            else
            {
                foreach (var s in list.Sessions)
                {
                    Console.WriteLine($"{s.Name}: {s.WindowCount} windows (created {s.CreatedAt:yyyy-MM-dd HH:mm:ss}) [{s.AttachedClients} clients]");
                }
            }
        }
    }

    static void KillServer()
    {
        using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", WmuxServer.PipeName, System.IO.Pipes.PipeDirection.InOut);
        pipe.Connect(3000);
        IpcProtocol.Send(pipe, new KillServerMessage());
        Console.WriteLine("wmux server killed.");
    }

    static void PrintHelp()
    {
        Console.WriteLine(@"wmux - Terminal Multiplexer for Windows

Usage:
  wmux                        Start server + create/attach session ""0""
  wmux new-session [-s name]  Create a new session (auto-named 0,1,2,...)
  wmux attach [name] [-t name]  Attach to an existing session
  wmux start-server           Start a standalone background server
  wmux list-sessions          List server sessions
  wmux kill-server            Stop the server
  wmux help                   Show this help

Key Bindings (after Ctrl+A prefix):
  s / S     Split pane horizontally (top/bottom)
  | / v     Split pane vertically (left/right)
  Arrow     Navigate between panes
  c         New window
  n / p     Next / previous window
  0-9       Select window by number
  d         Detach from session
  x         Kill pane
  :         Command mode
  ,         Rename window
  &         Kill window
  o         Next pane
  Space     Cycle layout presets

Ctrl+D      Close current pane (or window/session if last pane)

Command Mode:
  split-window [-h|-v]    Split the current pane
  new-window [name]       Create a new window
  kill-pane               Kill the active pane
  kill-window             Kill the active window
  select-window <n>       Select window by index
  rename-window <name>    Rename the active window
  select-layout <preset>  Apply layout preset
  list-windows            List all windows
  list-panes              List panes in current window");
    }
}
