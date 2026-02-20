using System.IO.Pipes;
using Wmux.Commands;
using Wmux.Core;

namespace Wmux.Server;

/// <summary>
/// Background server process that manages sessions and handles client connections
/// via named pipes. Broadcasts screen snapshots to all attached clients.
/// </summary>
public class WmuxServer
{
    public const string PipeName = "wmux-server";

    /// <summary>
    /// Lock file path used for server discovery. Contains the server PID.
    /// Avoids the race condition of probing the pipe (which consumes a connection).
    /// </summary>
    public static readonly string LockFilePath =
        Path.Combine(Path.GetTempPath(), "wmux-server.lock");

    private readonly Dictionary<int, Session> _sessions = new();
    private readonly List<ClientConnection> _clients = new();
    private readonly CommandRegistry _commands = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();
    private int _nextSessionId = 1;

    // Per-session dirty flag for coalesced broadcasting
    private readonly HashSet<int> _dirtySessions = new();

    // Tracks which pane IDs have been wired for events
    private readonly HashSet<int> _wiredPaneIds = new();

    // When true, auto-shutdown when last client disconnects
    public bool EmbeddedMode { get; set; }

    /// <summary>
    /// Signaled when the server is ready to accept connections.
    /// Used by embedded mode to avoid racing between server start and client connect.
    /// </summary>
    public ManualResetEventSlim Ready { get; } = new(false);

    public async Task RunAsync()
    {
        System.Diagnostics.Debug.WriteLine($"wmux server started (PID {Environment.ProcessId})");

        // Write lock file for server discovery
        WriteLockFile();

        // Start the broadcast loop
        _ = Task.Run(() => BroadcastLoop(_cts.Token));

        try
        {
            // Pre-create the first pipe so it's listening before we signal Ready.
            var pipeServer = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            // Signal that the server is ready to accept connections
            Ready.Set();

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await pipeServer.WaitForConnectionAsync(_cts.Token);
                    var client = new ClientConnection(pipeServer);
                    lock (_lock) { _clients.Add(client); }
                    _ = Task.Run(() => HandleClient(client));

                    // Create the next pipe for the next connection
                    pipeServer = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Server error: {ex.Message}");
                }
            }

            // Dispose the pending pipe that was never connected
            try { pipeServer.Dispose(); } catch { }
        }
        finally
        {
            RemoveLockFile();
        }
    }

    /// <summary>
    /// Broadcast loop: runs at ~60fps, checks for dirty sessions and sends
    /// screen snapshots to all attached clients.
    /// </summary>
    private async Task BroadcastLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(16, ct); // ~60fps
            }
            catch (OperationCanceledException) { break; }

            // Collect dirty sessions
            List<(int id, Session session)> dirtyList;
            lock (_lock)
            {
                if (_dirtySessions.Count == 0) continue;
                dirtyList = _dirtySessions
                    .Where(id => _sessions.ContainsKey(id))
                    .Select(id => (id, _sessions[id]))
                    .ToList();
                _dirtySessions.Clear();
            }

            foreach (var (sessionId, session) in dirtyList)
            {
                List<ClientConnection> targets;
                lock (_lock)
                {
                    targets = _clients
                        .Where(c => c.Session == session && c.IsConnected)
                        .ToList();
                }

                if (targets.Count == 0) continue;

                // Build and send a snapshot for each client at their dimensions
                foreach (var client in targets)
                {
                    try
                    {
                        ScreenSnapshotMessage snapshot;
                        lock (_lock)
                        {
                            if (session.Windows.Count == 0) continue;
                            snapshot = ServerRenderer.BuildSnapshot(
                                session, client.Width, client.Height);
                        }

                        lock (client.WriteLock)
                        {
                            IpcProtocol.Send(client.Pipe, snapshot);
                        }
                    }
                    catch
                    {
                        // Client disconnected — will be cleaned up by HandleClient
                    }
                }
            }
        }
    }

    private async Task HandleClient(ClientConnection client)
    {
        try
        {
            while (client.IsConnected && !_cts.Token.IsCancellationRequested)
            {
                var msg = await IpcProtocol.ReceiveAsync(client.Pipe, _cts.Token);
                if (msg == null) break;

                ProcessMessage(client, msg);
            }
        }
        catch (IOException) { }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            bool shouldShutdown = false;
            lock (_lock)
            {
                _clients.Remove(client);
                if (client.Session != null)
                    client.Session = null;

                // Auto-shutdown in embedded mode when last client disconnects
                if (EmbeddedMode && _clients.Count == 0)
                    shouldShutdown = true;
            }

            try { client.Dispose(); } catch { }

            if (shouldShutdown)
                Shutdown();
        }
    }

    private void ProcessMessage(ClientConnection client, IpcMessage msg)
    {
        switch (msg)
        {
            case NewSessionMessage nsm:
                HandleNewSession(client, nsm);
                break;
            case AttachMessage am:
                HandleAttach(client, am);
                break;
            case DetachMessage:
                client.Session = null;
                break;
            case ResizeMessage rm:
                HandleResize(client, rm);
                break;
            case InputMessage im:
                if (client.Session != null)
                {
                    lock (_lock)
                    {
                        client.Session.ActiveWindow.ActivePane.WriteInput(im.Data);
                    }
                }
                break;
            case CommandMessage cm:
                HandleCommand(client, cm);
                break;
            case SessionInfoMessage:
                SendSessionList(client);
                break;
            case KillServerMessage:
                Shutdown();
                break;
        }
    }

    /// <summary>
    /// Generate the lowest non-negative integer name not already used by a session.
    /// Produces "0", "1", "2", etc. (tmux-style). Must be called under _lock.
    /// </summary>
    private string GenerateSessionName()
    {
        var usedNames = new HashSet<string>(_sessions.Values.Select(s => s.Name));
        for (int n = 0; ; n++)
        {
            var candidate = n.ToString();
            if (!usedNames.Contains(candidate))
                return candidate;
        }
    }

    private void HandleNewSession(ClientConnection client, NewSessionMessage msg)
    {
        lock (_lock)
        {
            if (msg.ForceCreate)
            {
                // ForceCreate: always create a new session
                string name;
                if (!string.IsNullOrEmpty(msg.Name))
                {
                    // Explicit name — error if already exists
                    var existing = _sessions.Values.FirstOrDefault(s => s.Name == msg.Name);
                    if (existing != null)
                    {
                        lock (client.WriteLock)
                        {
                            IpcProtocol.Send(client.Pipe, new ErrorMessage
                            {
                                Text = $"duplicate session: {msg.Name}"
                            });
                        }
                        return;
                    }
                    name = msg.Name;
                }
                else
                {
                    name = GenerateSessionName();
                }

                var session = new Session(name, msg.Width, msg.Height - 1);
                _sessions[session.Id] = session;
                _nextSessionId++;
                client.Session = session;
                client.Width = msg.Width;
                client.Height = msg.Height;

                WireSessionPanes(session);
                MarkDirty(session);

                lock (client.WriteLock)
                {
                    IpcProtocol.Send(client.Pipe, new AttachMessage { SessionName = name });
                }
                SendImmediateSnapshot(client, session);
            }
            else
            {
                // CreateOrAttach: attach to existing session by name, or create new
                var name = string.IsNullOrEmpty(msg.Name) ? GenerateSessionName() : msg.Name;

                var existing = _sessions.Values.FirstOrDefault(s => s.Name == name);
                if (existing != null)
                {
                    client.Session = existing;
                    client.Width = msg.Width;
                    client.Height = msg.Height;
                    WireSessionPanes(existing);
                    MarkDirty(existing);

                    lock (client.WriteLock)
                    {
                        IpcProtocol.Send(client.Pipe, new AttachMessage { SessionName = name });
                    }
                    SendImmediateSnapshot(client, existing);
                    return;
                }

                var session = new Session(name, msg.Width, msg.Height - 1);
                _sessions[session.Id] = session;
                _nextSessionId++;
                client.Session = session;
                client.Width = msg.Width;
                client.Height = msg.Height;

                WireSessionPanes(session);
                MarkDirty(session);

                lock (client.WriteLock)
                {
                    IpcProtocol.Send(client.Pipe, new AttachMessage { SessionName = name });
                }
                SendImmediateSnapshot(client, session);
            }
        }
    }

    private void HandleAttach(ClientConnection client, AttachMessage msg)
    {
        lock (_lock)
        {
            Session? session = null;

            if (!string.IsNullOrEmpty(msg.SessionName))
            {
                session = _sessions.Values.FirstOrDefault(s => s.Name == msg.SessionName);

                // Fallback: try parsing as session ID
                if (session == null && int.TryParse(msg.SessionName, out int id))
                    _sessions.TryGetValue(id, out session);
            }
            else if (_sessions.Count > 0)
            {
                session = _sessions.Values.First();
            }

            if (session == null)
            {
                lock (client.WriteLock)
                {
                    IpcProtocol.Send(client.Pipe, new ErrorMessage { Text = "No matching session found" });
                }
                return;
            }

            client.Session = session;

            // If client hasn't sent dimensions yet, default to session dimensions
            if (client.Width == 0 || client.Height == 0)
            {
                client.Width = session.ActiveWindow.Width;
                client.Height = session.ActiveWindow.Height + 1; // +1 for status bar
            }

            WireSessionPanes(session);
            MarkDirty(session);

            lock (client.WriteLock)
            {
                IpcProtocol.Send(client.Pipe, new AttachMessage { SessionName = session.Name });
            }
            SendImmediateSnapshot(client, session);
        }
    }

    private void HandleResize(ClientConnection client, ResizeMessage rm)
    {
        if (client.Session == null) return;

        lock (_lock)
        {
            client.Width = rm.Width;
            client.Height = rm.Height;

            // Recalculate minimum dimensions across all clients for this session
            var sessionClients = _clients
                .Where(c => c.Session == client.Session && c.IsConnected)
                .ToList();

            if (sessionClients.Count == 0) return;

            int minW = sessionClients.Min(c => c.Width);
            int minH = sessionClients.Min(c => c.Height);

            client.Session.Resize(minW, minH - 1); // -1 for status bar
            MarkDirty(client.Session);
        }
    }

    private void HandleCommand(ClientConnection client, CommandMessage cm)
    {
        if (client.Session == null) return;

        string? result;
        Session? sessionToDestroy = null;
        lock (_lock)
        {
            var parsed = CommandParser.Parse(cm.Command);
            if (parsed == null) return;

            result = _commands.Execute(parsed, client.Session);

            // Check for special "destroy-session" signal from kill-pane/kill-window
            if (result == "\x01destroy-session")
            {
                sessionToDestroy = client.Session;
                result = null;
            }

            // After commands that may create new panes/windows, re-wire events
            if (sessionToDestroy == null)
            {
                WireSessionPanes(client.Session);
                MarkDirty(client.Session);
            }
        }

        // Destroy the session outside the lock to avoid deadlocks with pane disposal
        if (sessionToDestroy != null)
        {
            DestroySession(sessionToDestroy);
            return;
        }

        if (result != null)
        {
            try
            {
                lock (client.WriteLock)
                {
                    IpcProtocol.Send(client.Pipe, new CommandResultMessage { Result = result });
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Send a snapshot immediately to a single client so they don't have to
    /// wait for the next broadcast loop tick. Must be called under _lock.
    /// </summary>
    private void SendImmediateSnapshot(ClientConnection client, Session session)
    {
        try
        {
            if (session.Windows.Count == 0) return;
            var snapshot = ServerRenderer.BuildSnapshot(session, client.Width, client.Height);
            lock (client.WriteLock)
            {
                IpcProtocol.Send(client.Pipe, snapshot);
            }
        }
        catch { /* Client may have disconnected */ }
    }

    /// <summary>
    /// Wire OutputReceived and ProcessExited events for all panes in a session.
    /// Uses _wiredPaneIds to avoid duplicate subscriptions.
    /// </summary>
    private void WireSessionPanes(Session session)
    {
        foreach (var win in session.Windows)
        {
            foreach (var pane in win.GetPanes())
            {
                if (_wiredPaneIds.Add(pane.Id))
                {
                    pane.OutputReceived += _ => MarkDirty(session);
                    pane.ProcessExited += exitedPane => OnPaneExited(session, exitedPane);
                }
            }
        }
    }

    private void MarkDirty(Session session)
    {
        lock (_lock)
        {
            _dirtySessions.Add(session.Id);
        }
    }

    /// <summary>
    /// Handle a pane's process exiting. Closes the pane, window, or session
    /// as appropriate. If the session is destroyed, notifies all clients.
    /// </summary>
    private void OnPaneExited(Session session, Pane exitedPane)
    {
        bool destroy = false;
        lock (_lock)
        {
            if (!_sessions.ContainsValue(session)) return;

            // Find the owning window
            Window? ownerWindow = null;
            foreach (var win in session.Windows)
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
                ownerWindow.ClosePane(exitedPane);
                MarkDirty(session);
            }
            else if (session.Windows.Count > 1)
            {
                session.CloseWindow(ownerWindow);
                MarkDirty(session);
            }
            else
            {
                destroy = true;
            }
        }

        // Destroy outside of _lock to avoid blocking the broadcast loop
        // while session.Dispose() joins read threads.
        if (destroy)
            DestroySession(session);
    }

    /// <summary>
    /// Destroy a session and notify all attached clients.
    /// </summary>
    private void DestroySession(Session session)
    {
        List<ClientConnection> attachedClients;
        bool shouldCancel = false;

        lock (_lock)
        {
            // Notify all attached clients
            attachedClients = _clients
                .Where(c => c.Session == session && c.IsConnected)
                .ToList();

            foreach (var client in attachedClients)
            {
                try
                {
                    lock (client.WriteLock)
                    {
                        IpcProtocol.Send(client.Pipe, new SessionClosedMessage());
                    }
                }
                catch { }
                client.Session = null;
            }

            _sessions.Remove(session.Id);
            _dirtySessions.Remove(session.Id);

            if (EmbeddedMode && _sessions.Count == 0)
                shouldCancel = true;
        }

        // Dispose session outside of _lock (may block briefly on thread joins)
        try { session.Dispose(); } catch { }

        if (shouldCancel)
            _cts.Cancel();
    }

    private void SendSessionList(ClientConnection client)
    {
        lock (_lock)
        {
            var list = new SessionListMessage();
            foreach (var s in _sessions.Values)
            {
                list.Sessions.Add(new SessionEntry
                {
                    Id = s.Id,
                    Name = s.Name,
                    WindowCount = s.Windows.Count,
                    CreatedAt = s.CreatedAt,
                    AttachedClients = _clients.Count(c => c.Session == s)
                });
            }

            lock (client.WriteLock)
            {
                IpcProtocol.Send(client.Pipe, list);
            }
        }
    }

    private void Shutdown()
    {
        List<Session> toDispose;
        List<ClientConnection> clientsToNotify;
        lock (_lock)
        {
            toDispose = _sessions.Values.ToList();
            clientsToNotify = _clients.Where(c => c.IsConnected).ToList();
            _sessions.Clear();
            _dirtySessions.Clear();
        }

        // Notify all connected clients that their sessions are closing
        foreach (var client in clientsToNotify)
        {
            try
            {
                lock (client.WriteLock)
                {
                    IpcProtocol.Send(client.Pipe, new SessionClosedMessage());
                }
            }
            catch { }
        }

        // Dispose sessions outside of lock
        foreach (var session in toDispose)
        {
            try { session.Dispose(); } catch { }
        }

        RemoveLockFile();

        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Check whether a wmux server is running by reading the lock file
    /// and verifying the PID is still alive. This avoids consuming a pipe
    /// connection (which previously caused race conditions on attach).
    /// </summary>
    public static bool IsServerRunning()
    {
        try
        {
            if (!File.Exists(LockFilePath))
                return false;

            var content = File.ReadAllText(LockFilePath).Trim();
            if (!int.TryParse(content, out int pid))
                return false;

            // Check if the process is still alive AND is actually a wmux process
            try
            {
                var process = System.Diagnostics.Process.GetProcessById(pid);
                if (process.HasExited)
                {
                    RemoveLockFile();
                    return false;
                }

                // Verify process name to avoid PID reuse false positives
                var name = process.ProcessName.ToLowerInvariant();
                if (!name.Contains("wmux"))
                {
                    RemoveLockFile();
                    return false;
                }
                return true;
            }
            catch (ArgumentException)
            {
                // Process not found — stale lock file
                RemoveLockFile();
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static void WriteLockFile()
    {
        try
        {
            File.WriteAllText(LockFilePath, Environment.ProcessId.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to write lock file: {ex.Message}");
        }
    }

    private static void RemoveLockFile()
    {
        try
        {
            if (File.Exists(LockFilePath))
                File.Delete(LockFilePath);
        }
        catch { }
    }
}

public class ClientConnection : IDisposable
{
    public NamedPipeServerStream Pipe { get; }
    public Session? Session { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsConnected => Pipe.IsConnected;

    /// <summary>
    /// Lock for serializing writes to this client's pipe.
    /// Multiple threads (broadcast loop, command responses) may write concurrently.
    /// </summary>
    public readonly object WriteLock = new();

    public ClientConnection(NamedPipeServerStream pipe)
    {
        Pipe = pipe;
    }

    public void Dispose() => Pipe.Dispose();
}
