using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Pipeline;
using Vox.Core.Remote;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Remote;

/// <summary>Two Vox computers in one process, talking through a relay on this computer.</summary>
public class RemoteAccessSessionTests : IDisposable
{
    private const string Code = "ABCD-EFGH-JKMN";

    private sealed class Side : IDisposable
    {
        public Side(string name)
        {
            Queue = new SpeechQueue(new RecordingSpeechEngine(), NullLogger<SpeechQueue>.Instance);
            Queue.UtteranceQueued += (_, u) => { lock (Said) Said.Add(u.Text); };
            var hook = new Mock<IKeyboardHook>();
            hook.SetupProperty(h => h.ScreenReaderModifier, ModifierKey.Insert);
            Keys = new KeyInputDispatcher(hook.Object, KeyMap.LoadFromJson("""{ "bindings": [] }"""), new Mock<IEventSink>().Object,
                NullLogger<KeyInputDispatcher>.Instance);
            Session = new RemoteAccessSession(new RemoteTransport(RunPolicy.Normal), Queue, Keys,
                new RemoteKeyPlayer(Injector, () => ModifierKey.Insert), () => "Insert+F11", NullLogger<RemoteAccessSession>.Instance, name);
        }

        public SpeechQueue Queue { get; }
        public KeyInputDispatcher Keys { get; }
        public RecordingInjector Injector { get; } = new();
        public RemoteAccessSession Session { get; }
        public List<string> Said { get; } = [];

        public bool HasSaid(string text)
        {
            lock (Said) return Said.Any(s => s.Contains(text));
        }

        public void Dispose() => Session.Dispose();
    }

    internal sealed class RecordingInjector : IRemoteKeyInjector
    {
        private readonly List<(int Vk, bool Down)> _sent = [];
        public IReadOnlyList<(int Vk, bool Down)> Sent { get { lock (_sent) return _sent.ToList(); } }
        public void SendRaw(int vkCode, int scanCode, bool down, bool extended) { lock (_sent) _sent.Add((vkCode, down)); }
    }

    private readonly RemoteRelayServer _relay = new(NullLogger.Instance);
    private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(60));
    private readonly Side _student = new("STUDENT-PC");
    private readonly Side _trainer = new("TRAINER-PC");

    public RemoteAccessSessionTests()
    {
        _relay.Start(0, IPAddress.Loopback);
        _ = _relay.RunAsync(_stop.Token);
    }

    public void Dispose()
    {
        _student.Dispose();
        _trainer.Dispose();
        _relay.Dispose();
        _stop.Cancel();
        _stop.Dispose();
    }

    private RemoteEndpoint Relay => new(true, "127.0.0.1", _relay.Port);

    private async Task ConnectAsync()
    {
        _student.Session.StartControlled(Relay, Code);
        await RemoteTestSupport.WaitUntilAsync(() => _student.Session.State == RemoteState.Waiting, "the student to wait");
        await Task.Delay(200);
        _trainer.Session.Connect(Relay, "abcd efgh jkmn");
        await RemoteTestSupport.WaitUntilAsync(() => _student.HasSaid("TRAINER-PC can control") && _trainer.HasSaid("Connected to STUDENT-PC"),
            "both sides to say the session started");
    }

    [Fact]
    public async Task ASession_StartsWithAnAnnouncementOnBothSides()
    {
        await ConnectAsync();

        Assert.Equal(RemoteState.Connected, _student.Session.State);
        Assert.Equal(RemoteRole.Controlled, _student.Session.Role);
        Assert.Equal("STUDENT-PC", _trainer.Session.PeerName);
        Assert.True(_trainer.HasSaid("Press Insert+F11"));
    }

    [Fact]
    public async Task TheControllingSide_SendsKeys_OnlyWhileItControls()
    {
        await ConnectAsync();
        Assert.Null(_trainer.Keys.KeyForwarder);

        Assert.Equal("Controlling STUDENT-PC", _trainer.Session.ToggleControl());
        _trainer.Keys.KeyForwarder!(new KeyEvent { VkCode = 0x41, IsKeyDown = true });
        _trainer.Keys.KeyForwarder!(new KeyEvent { VkCode = 0x41, IsKeyDown = false });
        await RemoteTestSupport.WaitUntilAsync(() => _student.Injector.Sent.Count == 2, "the keys to arrive");

        Assert.Equal([(0x41, true), (0x41, false)], _student.Injector.Sent);
        Assert.Equal("Controlling this computer", _trainer.Session.ToggleControl());
        Assert.Null(_trainer.Keys.KeyForwarder);
    }

    [Fact]
    public async Task TheControlledSidesSpeech_IsHeardOnTheControllingSide()
    {
        await ConnectAsync();

        _student.Queue.Enqueue(new Utterance("Document 1 - Word", Core.Speech.SpeechPriority.Interrupt));

        await RemoteTestSupport.WaitUntilAsync(() => _trainer.HasSaid("Document 1 - Word"), "the speech to arrive");
    }

    [Fact]
    public async Task EndingTheSession_IsSaidOnBothSides_AndHeldKeysAreReleased()
    {
        await ConnectAsync();
        _trainer.Session.ToggleControl();
        _trainer.Keys.KeyForwarder!(new KeyEvent { VkCode = 0xA2, IsKeyDown = true });
        await RemoteTestSupport.WaitUntilAsync(() => _student.Injector.Sent.Count == 1, "the key to arrive");

        _trainer.Session.Disconnect();

        await RemoteTestSupport.WaitUntilAsync(() => _student.HasSaid("Remote session ended") && _trainer.HasSaid("Remote session ended"),
            "both sides to say the session ended");
        await RemoteTestSupport.WaitUntilAsync(() => _student.Session.State == RemoteState.Idle, "the student to be idle");
        Assert.Equal((0xA2, false), _student.Injector.Sent[^1]);
        Assert.Null(_trainer.Keys.KeyForwarder);
        Assert.Equal(RemoteState.Idle, _trainer.Session.State);
    }

    [Fact]
    public async Task TheWrongCode_FailsTheConnection_AndTheOtherSideKeepsWaiting()
    {
        _student.Session.StartControlled(Relay, Code);
        await RemoteTestSupport.WaitUntilAsync(() => _student.Session.State == RemoteState.Waiting, "the student to wait");
        await Task.Delay(200);

        _trainer.Session.Connect(Relay, "ABCD-EFGH-JKMP");

        await RemoteTestSupport.WaitUntilAsync(() => _trainer.Session.State == RemoteState.Idle && _trainer.HasSaid("Remote access failed"),
            "the trainer to give up");
        // The relay never paired them (another code is another channel): the student is still waiting
        Assert.Equal(RemoteState.Waiting, _student.Session.State);
    }

    [Fact]
    public async Task ADirectConnection_Works_Too()
    {
        var port = RemoteTestSupport.FreePort();
        _student.Session.StartControlled(new RemoteEndpoint(false, null, port), Code);
        await RemoteTestSupport.WaitUntilAsync(() => _student.Session.State == RemoteState.Waiting, "the student to wait");
        await Task.Delay(200);

        _trainer.Session.Connect(new RemoteEndpoint(false, "127.0.0.1", port), Code);

        await RemoteTestSupport.WaitUntilAsync(() => _trainer.Session.State == RemoteState.Connected && _trainer.HasSaid("Connected to"),
            "the direct session to start");
    }

    [Fact]
    public async Task ADirectConnectionWithTheWrongCode_IsSaidOnTheControlledSide()
    {
        var port = RemoteTestSupport.FreePort();
        _student.Session.StartControlled(new RemoteEndpoint(false, null, port), Code);
        await RemoteTestSupport.WaitUntilAsync(() => _student.Session.State == RemoteState.Waiting, "the student to wait");
        await Task.Delay(200);

        _trainer.Session.Connect(new RemoteEndpoint(false, "127.0.0.1", port), "ABCD-EFGH-JKMP");

        await RemoteTestSupport.WaitUntilAsync(() => _student.HasSaid("wrong session code"), "the student to hear of the wrong code");
        Assert.Equal(RemoteState.Waiting, _student.Session.State);
    }

    [Fact]
    public void Toggling_WithoutASession_SaysNothingToDo() => Assert.Null(_trainer.Session.ToggleControl());

    [Fact]
    public void OnlyOneSessionAtATime()
    {
        _student.Session.StartControlled(Relay, Code);

        Assert.Throws<InvalidOperationException>(() => _student.Session.Connect(Relay, Code));
    }
}
