using System.Diagnostics;
using System.Net.Sockets;
using Wmux.Client;
using Wmux.Config;
using Wmux.Server;

namespace Wmux;

static class Program
{
    static int Main(string[] args)
    {
        var config = WmuxConfig.Load();

        // Parse global options (before the sub-command)
        var remaining = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--activate" or "-A")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Error: --activate requires a value.");
                    return 1;
                }
                string act = args[++i];
                if (act.Length < 2)
                {
                    Console.Error.WriteLine("Error: activation string must be at least 2 characters.");
                    return 1;
                }
                foreach (char c in act)
                {
                    if (c < ' ' || char.IsControl(c))
                    {
                        Console.Error.WriteLine("Error: activation string must contain only printable characters (no Control keys).");
                        return 1;
                    }
                }
                config.Keys.ActivationString = act;
            }
            else
            {
                remaining.Add(args[i]);
            }
        }
        args = remaining.ToArray();

        if (args.Length == 0)
        {
            int port = WmuxServer.GetServerPort();
            if (port <= 0)
            {
                port = LaunchServerProcess();
                if (port <= 0)
                {
                    Console.Error.WriteLine("wmux server failed to start.");
                    return 1;
                }
            }

            var client = new WmuxClient(config);
            client.AttachToServer(mode: ClientMode.CreateOrAttach, port: port);
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

                // Ensure a server is running
                int port = WmuxServer.GetServerPort();
                if (port <= 0)
                {
                    port = LaunchServerProcess();
                    if (port <= 0)
                    {
                        Console.Error.WriteLine("wmux server failed to start.");
                        return 1;
                    }
                }

                var client = new WmuxClient(config);
                client.AttachToServer(name, ClientMode.ForceCreate, port);
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

                int port = WmuxServer.GetServerPort();
                if (port <= 0)
                {
                    Console.Error.WriteLine("No wmux server running. Run 'wmux' first.");
                    return 1;
                }

                var client2 = new WmuxClient(config);
                client2.AttachToServer(name, ClientMode.Attach, port);
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
                server.RunAsync().GetAwaiter().GetResult();
                return 0;
            }

            case "list-sessions" or "list-session" or "ls":
            {
                int port = WmuxServer.GetServerPort();
                if (port <= 0)
                {
                    Console.Error.WriteLine("no server running on localhost");
                    return 1;
                }
                ListSessions(port);
                return 0;
            }

            case "kill-server":
            {
                int port = WmuxServer.GetServerPort();
                if (port <= 0)
                {
                    Console.Error.WriteLine("No wmux server running.");
                    return 1;
                }
                KillServer(port);
                return 0;
            }

            case "help" or "--help" or "-h":
                PrintHelp();
                return 0;

            case "version" or "--version" or "-v":
                Console.WriteLine("wmux 0.0.2");
                return 0;

            default:
                Console.Error.WriteLine($"Unknown command: {command}");
                Console.Error.WriteLine();
                PrintHelp();
                return 1;
        }
    }

    /// <summary>
    /// Launch a wmux server as a detached background process and wait for it to be ready.
    /// Returns the server port, or -1 on failure.
    /// </summary>
    static int LaunchServerProcess()
    {
        var exePath = Environment.ProcessPath;
        var dllPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        string fileName, arguments;

        // Prefer launching via 'dotnet <dll>' — this avoids BadImageFormatException
        // issues that can occur when the apphost exe is launched with CreateNoWindow.
        if (!string.IsNullOrEmpty(dllPath) && File.Exists(dllPath))
        {
            // Find dotnet.exe: either it's our current process, or look it up
            string dotnet;
            if (exePath != null &&
                Path.GetFileNameWithoutExtension(exePath)
                    .Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                dotnet = exePath;
            }
            else
            {
                dotnet = "dotnet";
            }
            fileName = dotnet;
            arguments = $"\"{dllPath}\" start-server";
        }
        else if (exePath != null)
        {
            // Fallback: run the exe directly
            fileName = exePath;
            arguments = "start-server";
        }
        else
        {
            return -1;
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to start server process: {ex.Message}");
            return -1;
        }

        if (proc == null) return -1;

        // Poll for server readiness (lock file appears once listener is up)
        for (int i = 0; i < 50; i++) // 5 seconds max
        {
            Thread.Sleep(100);
            if (proc.HasExited)
            {
                var stderr = proc.StandardError.ReadToEnd().Trim();
                if (!string.IsNullOrEmpty(stderr))
                    Console.Error.WriteLine($"Server process failed: {stderr}");
                return -1;
            }
            int port = WmuxServer.GetServerPort();
            if (port > 0) return port;
        }
        return -1;
    }

    static void ListSessions(int port)
    {
        using var tcp = new TcpClient();
        tcp.Connect("127.0.0.1", port);
        tcp.NoDelay = true;
        var stream = tcp.GetStream();
        IpcProtocol.Send(stream, new SessionInfoMessage());
        var response = IpcProtocol.Receive(stream);
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

    static void KillServer(int port)
    {
        using var tcp = new TcpClient();
        tcp.Connect("127.0.0.1", port);
        tcp.NoDelay = true;
        var stream = tcp.GetStream();
        IpcProtocol.Send(stream, new KillServerMessage());
        Console.WriteLine("wmux server killed.");
    }

    static void PrintHelp()
    {
        Console.WriteLine(@"wmux - Terminal Multiplexer for Windows

Usage:
  wmux [options]                        Create/attach session (starts server if needed)
  wmux [options] new-session [-s name]  Create a new session
  wmux [options] attach [name] [-t name]  Attach to an existing session
  wmux start-server                     Start a standalone background server
  wmux list-sessions                    List server sessions
  wmux kill-server                      Stop the server
  wmux help                             Show this help

Options:
  --activate <str>, -A <str>  Set the activation string (default: ""za"").
                              Must be at least 2 printable characters.
                              Cannot contain Control keys.

Activation String:
  The activation string (default ""za"") enters prefix mode. Type the
  activation string followed by a command key. For example, with the
  default ""za"", type z then a then c to create a new window.

Key Bindings (after activation string):
  s / S     Split pane horizontally (top/bottom)
  | / v     Split pane vertically (left/right)
  Arrow     Navigate between panes
  c         New window
  r         Rename pane
  n / p     Next / previous window
  0-9       Select window by number
  d         Detach from session
  x         Kill pane
  :         Command mode
  ,         Rename window
  &         Kill window
  o         Next pane
  Space     Cycle layout presets

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
