using Vox.Core.AddOns;
using Vox.Sdk;

namespace Vox.Core.Tests.AddOns;

/// <summary>An add-on whose start the test writes; records whether it was disposed.</summary>
internal sealed class FakeAddOn(Action<IAddOnHost> initialize) : IAddOn, IDisposable
{
    public IAddOnHost? Host { get; private set; }
    public bool Disposed { get; private set; }

    public void Initialize(IAddOnHost host)
    {
        Host = host;
        initialize(host);
    }

    public void Dispose() => Disposed = true;
}

/// <summary>Hands out the test's add-on objects instead of loading assemblies.</summary>
internal sealed class FakeLoader : IAddOnLoader
{
    private readonly Dictionary<string, Func<IAddOn>> _addOns = new();

    public List<FakeLoaded> Loaded { get; } = [];

    /// <summary>What loading <paramref name="id"/> creates (each load a new one).</summary>
    public void Set(string id, Func<IAddOn> create) => _addOns[id] = create;

    public Exception? Throw { get; set; }

    public ILoadedAddOn Load(string directory, AddOnManifest manifest)
    {
        if (Throw is { } error)
            throw error;
        var instance = _addOns.TryGetValue(manifest.Id, out var create) ? create() : new FakeAddOn(_ => { });
        var loaded = new FakeLoaded(manifest.Id, instance);
        Loaded.Add(loaded);
        return loaded;
    }
}

internal sealed class FakeLoaded(string id, IAddOn instance) : ILoadedAddOn
{
    public string Id { get; } = id;
    public IAddOn Instance { get; } = instance;
    public bool Unloaded { get; private set; }
    public void Unload() => Unloaded = true;
}

/// <summary>An app module whose parts the test sets.</summary>
internal sealed class FakeSdkModule(string id, params string[] processes) : Sdk.AppModule
{
    public override string Id => id;
    public override string Name => "Fake " + id;
    public override IReadOnlyCollection<string> ProcessNames => processes;
    public List<AddOnCommand> CommandList { get; } = [];
    public override IReadOnlyList<AddOnCommand> Commands => CommandList;
    public List<string> Log { get; } = [];
    public override void OnActivated(AppInfo app) { base.OnActivated(app); Log.Add("on " + app.ProcessName); }
    public override void OnDeactivated() { base.OnDeactivated(); Log.Add("off"); }
}

/// <summary>A speech engine an add-on brings.</summary>
internal sealed class FakeSdkEngine : Sdk.ISpeechEngine
{
    public List<string> Spoken { get; } = [];
    public bool Disposed { get; private set; }
    public bool IsSpeaking => false;
    public Task SpeakAsync(string text, SpeakOptions options, CancellationToken cancellationToken)
    {
        Spoken.Add(text);
        return Task.CompletedTask;
    }
    public void Cancel() { }
    public void SetRate(int wordsPerMinute) { }
    public void SetPitch(int pitch) { }
    public void SetVolume(int volume) { }
    public void SetVoice(string voiceName) { }
    public IReadOnlyList<VoiceInfo> GetVoices() => [new VoiceInfo("Robot", "en-GB")];
    public void Dispose() => Disposed = true;
}

/// <summary>A braille driver an add-on brings.</summary>
internal sealed class FakeSdkBrailleDriver : IBrailleDriver
{
    public string Id => "FakeDots";
    public string Name => "Fake dots display";
    public bool IsConnected { get; private set; }
    public int CellCount => IsConnected ? 20 : 0;
    public bool Disposed { get; private set; }
    public bool TryConnect() => IsConnected = true;
    public void Disconnect() => IsConnected = false;
    public void Write(ReadOnlyMemory<byte> cells, int? cursorPosition) { }
    public event EventHandler<BrailleKeysEventArgs>? KeysPressed;
    public event EventHandler? Disconnected;
    public void Press(params string[] keys) => KeysPressed?.Invoke(this, new BrailleKeysEventArgs(keys));
    public void Lose() => Disconnected?.Invoke(this, EventArgs.Empty);
    public void Dispose() => Disposed = true;
}
