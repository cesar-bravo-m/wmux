using Wmux.Server;

namespace Wmux.Tests;

[TestFixture]
public class IpcProtocolTests
{
    // ── Serialization roundtrip ──────────────────────────────────────

    [Test]
    public void AttachMessage_Roundtrip()
    {
        var msg = new AttachMessage { SessionName = "test" };
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json) as AttachMessage;

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.SessionName, Is.EqualTo("test"));
    }

    [Test]
    public void DetachMessage_Roundtrip()
    {
        var msg = new DetachMessage();
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json);

        Assert.That(deserialized, Is.InstanceOf<DetachMessage>());
    }

    [Test]
    public void ResizeMessage_Roundtrip()
    {
        var msg = new ResizeMessage { Width = 120, Height = 40 };
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json) as ResizeMessage;

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Width, Is.EqualTo(120));
        Assert.That(deserialized.Height, Is.EqualTo(40));
    }

    [Test]
    public void InputMessage_Roundtrip()
    {
        var msg = new InputMessage { Data = "hello\r\n" };
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json) as InputMessage;

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Data, Is.EqualTo("hello\r\n"));
    }

    [Test]
    public void OutputMessage_Roundtrip()
    {
        var msg = new OutputMessage { PaneId = 42, Data = "screen data" };
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json) as OutputMessage;

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.PaneId, Is.EqualTo(42));
        Assert.That(deserialized.Data, Is.EqualTo("screen data"));
    }

    [Test]
    public void CommandMessage_Roundtrip()
    {
        var msg = new CommandMessage { Command = "split-window -v" };
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json) as CommandMessage;

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Command, Is.EqualTo("split-window -v"));
    }

    [Test]
    public void CommandResultMessage_Roundtrip()
    {
        var msg = new CommandResultMessage { Result = "pane killed" };
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json) as CommandResultMessage;

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Result, Is.EqualTo("pane killed"));
    }

    [Test]
    public void CommandResultMessage_NullResult_Roundtrip()
    {
        var msg = new CommandResultMessage { Result = null };
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json) as CommandResultMessage;

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Result, Is.Null);
    }

    [Test]
    public void SessionListMessage_Roundtrip()
    {
        var msg = new SessionListMessage
        {
            Sessions =
            [
                new SessionEntry
                {
                    Id = 1,
                    Name = "main",
                    WindowCount = 3,
                    CreatedAt = new DateTime(2025, 1, 15, 10, 30, 0),
                    AttachedClients = 2
                }
            ]
        };
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json) as SessionListMessage;

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Sessions, Has.Count.EqualTo(1));
        Assert.That(deserialized.Sessions[0].Name, Is.EqualTo("main"));
        Assert.That(deserialized.Sessions[0].WindowCount, Is.EqualTo(3));
        Assert.That(deserialized.Sessions[0].AttachedClients, Is.EqualTo(2));
    }

    [Test]
    public void NewSessionMessage_Roundtrip()
    {
        var msg = new NewSessionMessage { Name = "dev", Width = 200, Height = 50 };
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json) as NewSessionMessage;

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Name, Is.EqualTo("dev"));
        Assert.That(deserialized.Width, Is.EqualTo(200));
        Assert.That(deserialized.Height, Is.EqualTo(50));
    }

    [Test]
    public void KillServerMessage_Roundtrip()
    {
        var msg = new KillServerMessage();
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json);

        Assert.That(deserialized, Is.InstanceOf<KillServerMessage>());
    }

    [Test]
    public void ErrorMessage_Roundtrip()
    {
        var msg = new ErrorMessage { Text = "Session not found" };
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json) as ErrorMessage;

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Text, Is.EqualTo("Session not found"));
    }

    [Test]
    public void SessionInfoMessage_Roundtrip()
    {
        var msg = new SessionInfoMessage();
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json);

        Assert.That(deserialized, Is.InstanceOf<SessionInfoMessage>());
    }

    // ── IpcProtocol Send/Receive over MemoryStream ───────────────────

    [Test]
    public void SendReceive_Sync_Roundtrip()
    {
        var ms = new MemoryStream();
        var msg = new InputMessage { Data = "test data" };

        IpcProtocol.Send(ms, msg);

        ms.Position = 0;
        var received = IpcProtocol.Receive(ms);

        Assert.That(received, Is.InstanceOf<InputMessage>());
        Assert.That(((InputMessage)received!).Data, Is.EqualTo("test data"));
    }

    [Test]
    public async Task SendReceive_Async_Roundtrip()
    {
        var ms = new MemoryStream();
        var msg = new ResizeMessage { Width = 100, Height = 50 };

        await IpcProtocol.SendAsync(ms, msg);

        ms.Position = 0;
        var received = await IpcProtocol.ReceiveAsync(ms);

        Assert.That(received, Is.InstanceOf<ResizeMessage>());
        var rm = (ResizeMessage)received!;
        Assert.That(rm.Width, Is.EqualTo(100));
        Assert.That(rm.Height, Is.EqualTo(50));
    }

    [Test]
    public void SendReceive_MultipleMessages()
    {
        var ms = new MemoryStream();

        IpcProtocol.Send(ms, new InputMessage { Data = "first" });
        IpcProtocol.Send(ms, new InputMessage { Data = "second" });
        IpcProtocol.Send(ms, new InputMessage { Data = "third" });

        ms.Position = 0;

        var m1 = IpcProtocol.Receive(ms) as InputMessage;
        var m2 = IpcProtocol.Receive(ms) as InputMessage;
        var m3 = IpcProtocol.Receive(ms) as InputMessage;

        Assert.That(m1!.Data, Is.EqualTo("first"));
        Assert.That(m2!.Data, Is.EqualTo("second"));
        Assert.That(m3!.Data, Is.EqualTo("third"));
    }

    [Test]
    public void Receive_EmptyStream_ReturnsNull()
    {
        var ms = new MemoryStream();
        var result = IpcProtocol.Receive(ms);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Receive_TruncatedLength_ReturnsNull()
    {
        var ms = new MemoryStream(new byte[] { 0x05, 0x00 }); // Only 2 bytes of length
        var result = IpcProtocol.Receive(ms);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Receive_TruncatedBody_ReturnsNull()
    {
        var ms = new MemoryStream();
        // Write length header saying 1000 bytes
        ms.Write(BitConverter.GetBytes(1000));
        // Only write 5 bytes of body
        ms.Write(new byte[] { 1, 2, 3, 4, 5 });
        ms.Position = 0;

        var result = IpcProtocol.Receive(ms);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void SendReceive_MixedMessageTypes()
    {
        var ms = new MemoryStream();

        IpcProtocol.Send(ms, new AttachMessage { SessionName = "s1" });
        IpcProtocol.Send(ms, new ResizeMessage { Width = 80, Height = 24 });
        IpcProtocol.Send(ms, new DetachMessage());

        ms.Position = 0;

        Assert.That(IpcProtocol.Receive(ms), Is.InstanceOf<AttachMessage>());
        Assert.That(IpcProtocol.Receive(ms), Is.InstanceOf<ResizeMessage>());
        Assert.That(IpcProtocol.Receive(ms), Is.InstanceOf<DetachMessage>());
    }

    // ── Edge cases ───────────────────────────────────────────────────

    [Test]
    public void AttachMessage_NullSessionName()
    {
        var msg = new AttachMessage { SessionName = null };
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json) as AttachMessage;

        Assert.That(deserialized!.SessionName, Is.Null);
    }

    [Test]
    public void InputMessage_EmptyData()
    {
        var msg = new InputMessage { Data = "" };
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json) as InputMessage;

        Assert.That(deserialized!.Data, Is.EqualTo(""));
    }

    [Test]
    public void InputMessage_SpecialCharacters()
    {
        var msg = new InputMessage { Data = "\x1b[31m\r\n\t\0" };
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json) as InputMessage;

        Assert.That(deserialized!.Data, Is.EqualTo("\x1b[31m\r\n\t\0"));
    }

    [Test]
    public void SessionListMessage_EmptyList()
    {
        var msg = new SessionListMessage { Sessions = [] };
        var json = msg.Serialize();
        var deserialized = IpcMessage.Deserialize(json) as SessionListMessage;

        Assert.That(deserialized!.Sessions, Is.Empty);
    }
}
