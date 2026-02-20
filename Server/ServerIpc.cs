using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wmux.Server;

/// <summary>
/// IPC message protocol over named pipes. JSON-based messages.
/// </summary>
[JsonDerivedType(typeof(AttachMessage), typeDiscriminator: "attach")]
[JsonDerivedType(typeof(DetachMessage), typeDiscriminator: "detach")]
[JsonDerivedType(typeof(ResizeMessage), typeDiscriminator: "resize")]
[JsonDerivedType(typeof(InputMessage), typeDiscriminator: "input")]
[JsonDerivedType(typeof(OutputMessage), typeDiscriminator: "output")]
[JsonDerivedType(typeof(CommandMessage), typeDiscriminator: "command")]
[JsonDerivedType(typeof(CommandResultMessage), typeDiscriminator: "command_result")]
[JsonDerivedType(typeof(SessionListMessage), typeDiscriminator: "session_list")]
[JsonDerivedType(typeof(SessionInfoMessage), typeDiscriminator: "session_info")]
[JsonDerivedType(typeof(NewSessionMessage), typeDiscriminator: "new_session")]
[JsonDerivedType(typeof(KillServerMessage), typeDiscriminator: "kill_server")]
[JsonDerivedType(typeof(ErrorMessage), typeDiscriminator: "error")]
[JsonDerivedType(typeof(ScreenSnapshotMessage), typeDiscriminator: "screen_snapshot")]
[JsonDerivedType(typeof(SessionClosedMessage), typeDiscriminator: "session_closed")]
public abstract class IpcMessage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public string Serialize()
    {
        return JsonSerializer.Serialize<IpcMessage>(this, Options);
    }

    public static IpcMessage? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<IpcMessage>(json, Options);
    }
}

public class AttachMessage : IpcMessage
{
    public string? SessionName { get; set; }
}

public class DetachMessage : IpcMessage { }

public class ResizeMessage : IpcMessage
{
    public int Width { get; set; }
    public int Height { get; set; }
}

public class InputMessage : IpcMessage
{
    public string Data { get; set; } = "";
}

public class OutputMessage : IpcMessage
{
    // Serialized screen state for rendering
    public int PaneId { get; set; }
    public string Data { get; set; } = "";
}

public class CommandMessage : IpcMessage
{
    public string Command { get; set; } = "";
}

public class CommandResultMessage : IpcMessage
{
    public string? Result { get; set; }
}

public class SessionListMessage : IpcMessage
{
    public List<SessionEntry> Sessions { get; set; } = new();
}

public class SessionEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int WindowCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public int AttachedClients { get; set; }
}

public class SessionInfoMessage : IpcMessage { }

public class NewSessionMessage : IpcMessage
{
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public bool ForceCreate { get; set; }
}

public class KillServerMessage : IpcMessage { }

public class ErrorMessage : IpcMessage
{
    public string Text { get; set; } = "";
}

/// <summary>
/// Carries a fully composed screen grid from server to client.
/// The server renders the session state (panes, borders, status bar)
/// into a flat grid and sends it to all attached clients.
/// </summary>
public class ScreenSnapshotMessage : IpcMessage
{
    /// <summary>Grid width in columns.</summary>
    public int Width { get; set; }
    /// <summary>Grid height in rows.</summary>
    public int Height { get; set; }

    /// <summary>Flat character grid (row-major, length = Width*Height).</summary>
    public string Chars { get; set; } = "";

    /// <summary>Foreground ConsoleColor values (0-15), one byte per cell, Base64-encoded.</summary>
    public byte[] Fg { get; set; } = [];

    /// <summary>Background ConsoleColor values (0-15), one byte per cell, Base64-encoded.</summary>
    public byte[] Bg { get; set; } = [];

    public int CursorRow { get; set; }
    public int CursorCol { get; set; }
    public bool CursorVisible { get; set; }
}

/// <summary>
/// Sent to all clients when the session is destroyed (last pane exited).
/// Client should close its window.
/// </summary>
public class SessionClosedMessage : IpcMessage { }

/// <summary>
/// Helper for reading/writing length-prefixed JSON messages over streams.
/// </summary>
public static class IpcProtocol
{
    public static async Task SendAsync(Stream stream, IpcMessage message, CancellationToken ct = default)
    {
        var json = message.Serialize();
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var lenBytes = BitConverter.GetBytes(bytes.Length);
        await stream.WriteAsync(lenBytes, ct);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    public static void Send(Stream stream, IpcMessage message)
    {
        var json = message.Serialize();
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var lenBytes = BitConverter.GetBytes(bytes.Length);
        stream.Write(lenBytes);
        stream.Write(bytes);
        stream.Flush();
    }

    public static async Task<IpcMessage?> ReceiveAsync(Stream stream, CancellationToken ct = default)
    {
        var lenBuf = new byte[4];
        int read = await ReadExactAsync(stream, lenBuf, 4, ct);
        if (read < 4) return null;

        int len = BitConverter.ToInt32(lenBuf);
        if (len <= 0 || len > 10_000_000) return null;

        var buf = new byte[len];
        read = await ReadExactAsync(stream, buf, len, ct);
        if (read < len) return null;

        var json = System.Text.Encoding.UTF8.GetString(buf);
        return IpcMessage.Deserialize(json);
    }

    public static IpcMessage? Receive(Stream stream)
    {
        var lenBuf = new byte[4];
        int read = ReadExact(stream, lenBuf, 4);
        if (read < 4) return null;

        int len = BitConverter.ToInt32(lenBuf);
        if (len <= 0 || len > 10_000_000) return null;

        var buf = new byte[len];
        read = ReadExact(stream, buf, len);
        if (read < len) return null;

        var json = System.Text.Encoding.UTF8.GetString(buf);
        return IpcMessage.Deserialize(json);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buf, int count, CancellationToken ct)
    {
        int total = 0;
        while (total < count)
        {
            int n = await stream.ReadAsync(buf.AsMemory(total, count - total), ct);
            if (n == 0) return total;
            total += n;
        }
        return total;
    }

    private static int ReadExact(Stream stream, byte[] buf, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = stream.Read(buf, total, count - total);
            if (n == 0) return total;
            total += n;
        }
        return total;
    }
}
