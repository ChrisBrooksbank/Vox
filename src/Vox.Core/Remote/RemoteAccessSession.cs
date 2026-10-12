using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Vox.Core.Braille;
using Vox.Core.Input;
using Vox.Core.Localization;
using Vox.Core.Speech;

namespace Vox.Core.Remote;

/// <summary>Where a remote session is.</summary>
public enum RemoteState
{
    Idle,
    /// <summary>This computer waits for the controlling one (listening, or at the relay).</summary>
    Waiting,
    /// <summary>Connecting to the computer to control.</summary>
    Connecting,
    Connected,
}

/// <summary>
/// Remote access between two Vox computers (a trainer and a student, help from a colleague). The
/// controlled computer waits with a session code; the controlling one connects with it. While
/// connected the controlling side switches with <see cref="NavigationCommand.ToggleRemoteControl"/>
/// between sending its keys over (every key but that one) and using its own computer, and hears
/// (and sees on its braille display) what the controlled computer says. Both sides say when the
/// session starts and ends. Everything goes through a <see cref="SecureChannel"/>.
/// </summary>
public sealed class RemoteAccessSession : IDisposable
{
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(45);
    /// <summary>Wrong codes in a row after which the controlled computer stops waiting.</summary>
    public const int MaxWrongCodes = 5;

    private readonly RemoteTransport _transport;
    private readonly SpeechQueue _speech;
    private readonly KeyInputDispatcher _keys;
    private readonly RemoteKeyPlayer _player;
    private readonly Func<string> _keyNameOfToggle;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private readonly string _machineName;
    private CancellationTokenSource? _session;
    private Channel<RemoteMessage>? _outgoing;
    private BrailleController? _braille;
    private string? _lastBraille;
    private volatile RemoteState _state;
    private volatile bool _controlling;

    public RemoteAccessSession(RemoteTransport transport, SpeechQueue speech, KeyInputDispatcher keys, RemoteKeyPlayer player,
        Func<string> keyNameOfToggle, ILogger<RemoteAccessSession> logger, string? machineName = null)
    {
        _transport = transport;
        _speech = speech;
        _keys = keys;
        _player = player;
        _keyNameOfToggle = keyNameOfToggle;
        _logger = logger;
        _machineName = machineName ?? Environment.MachineName;
    }

    public RemoteState State => _state;

    /// <summary>This side's role while a session is waiting or connected.</summary>
    public RemoteRole? Role { get; private set; }

    /// <summary>The other computer's name once connected.</summary>
    public string? PeerName { get; private set; }

    /// <summary>The controlling side is sending its keys over.</summary>
    public bool IsControlling => _controlling;

    /// <summary>Raised (on any thread) when the state changes.</summary>
    public event EventHandler<RemoteState>? StateChanged;

    /// <summary>The braille controller (set by the braille service): the controlled side sends its line, the controlling side shows the remote one.</summary>
    public BrailleController? Braille
    {
        get => _braille;
        set
        {
            if (_braille is not null)
                _braille.LineShown -= OnLineShown;
            _braille = value;
            if (value is not null)
                value.LineShown += OnLineShown;
        }
    }

    /// <summary>
    /// Lets this computer be controlled: waits (directly on <see cref="RemoteEndpoint.Port"/>, or at the
    /// relay) for the computer with <paramref name="code"/>, then runs the session in the background.
    /// </summary>
    public void StartControlled(RemoteEndpoint endpoint, string code) =>
        Begin(RemoteRole.Controlled, RemoteState.Waiting, token => WaitForControllerAsync(endpoint, code, token));

    /// <summary>Connects to the computer to control; the session runs in the background.</summary>
    public void Connect(RemoteEndpoint endpoint, string code) =>
        Begin(RemoteRole.Controlling, RemoteState.Connecting, token => ConnectToControlledAsync(endpoint, code, token));

    /// <summary>The running session's task (tests wait on it).</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

    private void Begin(RemoteRole role, RemoteState state, Func<CancellationToken, Task> run)
    {
        CancellationTokenSource session;
        lock (_lock)
        {
            if (_state != RemoteState.Idle)
                throw new InvalidOperationException("A remote session is already running.");
            session = _session = new CancellationTokenSource();
            Role = role;
        }
        SetState(state);
        Running = Task.Run(async () =>
        {
            try { await run(session.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (session.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Remote session failed");
                Say(VoxStrings.Format("Remote.Failed", Describe(ex)), SpeechPriority.High);
            }
            finally
            {
                End();
            }
        });
    }

    private async Task WaitForControllerAsync(RemoteEndpoint endpoint, string code, CancellationToken token)
    {
        int wrong = 0;
        while (!token.IsCancellationRequested)
        {
            var stream = endpoint.Relay
                ? await _transport.ConnectThroughRelayAsync(endpoint.Host!, endpoint.Port, code, RemoteRole.Controlled, token).ConfigureAwait(false)
                : await _transport.ListenAsync(endpoint.Port, token).ConfigureAwait(false);
            SecureChannel channel;
            try
            {
                channel = await SecureChannel.EstablishAsync(stream, RemoteRole.Controlled, code, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is RemoteAuthenticationException or IOException or CryptographicException)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                _logger.LogWarning("A computer tried to connect for remote control and failed: {Reason}", ex.Message);
                if (++wrong >= MaxWrongCodes)
                {
                    Say(VoxStrings.Get("Remote.TooManyWrongCodes"), SpeechPriority.High);
                    return;
                }
                Say(VoxStrings.Get("Remote.WrongCode"), SpeechPriority.High);
                // Guessing codes stays slow
                await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
                continue;
            }
            await RunAsync(channel, token).ConfigureAwait(false);
            return;
        }
    }

    private async Task ConnectToControlledAsync(RemoteEndpoint endpoint, string code, CancellationToken token)
    {
        var stream = endpoint.Relay
            ? await _transport.ConnectThroughRelayAsync(endpoint.Host!, endpoint.Port, code, RemoteRole.Controlling, token).ConfigureAwait(false)
            : await _transport.ConnectAsync(endpoint.Host!, endpoint.Port, token).ConfigureAwait(false);
        SecureChannel channel;
        try
        {
            channel = await SecureChannel.EstablishAsync(stream, RemoteRole.Controlling, code, token).ConfigureAwait(false);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        await RunAsync(channel, token).ConfigureAwait(false);
    }

    /// <summary>The connected session: sends, receives and pings until either side ends it.</summary>
    private async Task RunAsync(SecureChannel channel, CancellationToken token)
    {
        await using var _ = channel;
        var outgoing = Channel.CreateBounded<RemoteMessage>(new BoundedChannelOptions(2000) { FullMode = BoundedChannelFullMode.DropOldest });
        using var connected = CancellationTokenSource.CreateLinkedTokenSource(token);
        lock (_lock) _outgoing = outgoing;
        SetState(RemoteState.Connected);
        outgoing.Writer.TryWrite(new RemoteMessage { Type = RemoteMessage.Hello, Name = _machineName });
        if (Role == RemoteRole.Controlled)
            _speech.UtteranceQueued += OnUtteranceQueued;

        var sending = SendLoopAsync(channel, outgoing.Reader, connected.Token);
        var keepingAlive = KeepAliveLoopAsync(outgoing.Writer, connected.Token);
        try
        {
            await ReceiveLoopAsync(channel, connected.Token).ConfigureAwait(false);
        }
        finally
        {
            if (Role == RemoteRole.Controlled)
                _speech.UtteranceQueued -= OnUtteranceQueued;
            lock (_lock) _outgoing = null;
            // A last goodbye, so the other side says the session ended at once
            try
            {
                using var bye = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await channel.SendAsync(new RemoteMessage { Type = RemoteMessage.Bye }.ToBytes(), bye.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException) { }
            connected.Cancel();
            outgoing.Writer.TryComplete();
            try { await Task.WhenAll(sending, keepingAlive).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
        }
    }

    private async Task ReceiveLoopAsync(SecureChannel channel, CancellationToken token)
    {
        bool announced = false;
        while (!token.IsCancellationRequested)
        {
            byte[]? bytes;
            using (var silence = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                silence.CancelAfter(SilenceLimit);
                try
                {
                    bytes = await channel.ReceiveAsync(silence.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    _logger.LogInformation("Remote session timed out");
                    Say(VoxStrings.Get("Remote.Lost"), SpeechPriority.High);
                    return;
                }
                catch (Exception ex) when (ex is IOException or CryptographicException)
                {
                    _logger.LogInformation(ex, "Remote session connection lost");
                    Say(VoxStrings.Get("Remote.Lost"), SpeechPriority.High);
                    return;
                }
            }
            if (bytes is null || RemoteMessage.FromBytes(bytes) is not { } message)
            {
                if (bytes is null)
                    return;
                continue;
            }
            switch (message.Type)
            {
                case RemoteMessage.Hello:
                    PeerName = string.IsNullOrWhiteSpace(message.Name) ? VoxStrings.Get("Remote.OtherComputer") : message.Name.Trim()[..Math.Min(64, message.Name.Trim().Length)];
                    if (!announced)
                    {
                        announced = true;
                        Say(Role == RemoteRole.Controlled
                            ? VoxStrings.Format("Remote.StartedControlled", PeerName)
                            : VoxStrings.Format("Remote.StartedControlling", PeerName, _keyNameOfToggle()), SpeechPriority.High);
                    }
                    break;
                case RemoteMessage.Bye:
                    return;
                case RemoteMessage.Key when Role == RemoteRole.Controlled:
                    _player.Play(message);
                    break;
                case RemoteMessage.Control when Role == RemoteRole.Controlled:
                    if (!message.On)
                        _player.ReleaseAll();
                    break;
                case RemoteMessage.Speak when Role == RemoteRole.Controlling && !string.IsNullOrWhiteSpace(message.Text):
                    _speech.Enqueue(new Utterance(message.Text, ParsePriority(message.Priority)));
                    break;
                case RemoteMessage.Braille when Role == RemoteRole.Controlling && _controlling && message.Text is not null:
                    _braille?.ShowReviewLine(message.Text, Math.Clamp(message.Cursor ?? 0, 0, message.Text.Length));
                    break;
            }
        }
    }

    private static async Task SendLoopAsync(SecureChannel channel, ChannelReader<RemoteMessage> outgoing, CancellationToken token)
    {
        await foreach (var message in outgoing.ReadAllAsync(token).ConfigureAwait(false))
            await channel.SendAsync(message.ToBytes(), token).ConfigureAwait(false);
    }

    private static async Task KeepAliveLoopAsync(ChannelWriter<RemoteMessage> outgoing, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(KeepAliveInterval, token).ConfigureAwait(false);
            outgoing.TryWrite(new RemoteMessage { Type = RemoteMessage.KeepAlive });
        }
    }

    /// <summary>
    /// The controlling side: starts or stops sending keys to the other computer. Returns what to
    /// say, or null when not connected as the controlling side.
    /// </summary>
    public string? ToggleControl()
    {
        if (_state != RemoteState.Connected || Role != RemoteRole.Controlling)
            return null;
        var on = !_controlling;
        _controlling = on;
        _keys.KeyForwarder = on ? Forward : null;
        Send(new RemoteMessage { Type = RemoteMessage.Control, On = on });
        return on ? VoxStrings.Format("Remote.ControlOn", PeerName ?? VoxStrings.Get("Remote.OtherComputer"))
            : VoxStrings.Get("Remote.ControlOff");
    }

    /// <summary>Ends the session (either side), or stops waiting.</summary>
    public void Disconnect()
    {
        CancellationTokenSource? session;
        lock (_lock) session = _session;
        session?.Cancel();
    }

    private void Forward(KeyEvent key) => Send(new RemoteMessage
    {
        Type = RemoteMessage.Key,
        Vk = key.VkCode,
        Scan = key.ScanCode,
        Down = key.IsKeyDown,
        Keypad = key.IsKeypad,
        Extended = key.IsExtended,
        VoxKey = _keys.IsScreenReaderKey(key),
    });

    private void Send(RemoteMessage message)
    {
        Channel<RemoteMessage>? outgoing;
        lock (_lock) outgoing = _outgoing;
        outgoing?.Writer.TryWrite(message);
    }

    // The controlled side's speech goes to the controlling side too
    private void OnUtteranceQueued(object? sender, Utterance utterance)
    {
        if (!string.IsNullOrWhiteSpace(utterance.Text))
            Send(new RemoteMessage { Type = RemoteMessage.Speak, Text = utterance.Text, Priority = utterance.Priority.ToString() });
    }

    // ... and its braille line (only while connected as the controlled side)
    private void OnLineShown(object? sender, BrailleLine line)
    {
        if (_state != RemoteState.Connected || Role != RemoteRole.Controlled)
            return;
        var key = $"{line.CursorOffset}:{line.Text}";
        if (key == _lastBraille)
            return;
        _lastBraille = key;
        Send(new RemoteMessage { Type = RemoteMessage.Braille, Text = line.Text, Cursor = line.CursorOffset });
    }

    private void End()
    {
        bool wasConnected = _state == RemoteState.Connected;
        _keys.KeyForwarder = null;
        _controlling = false;
        _player.ReleaseAll();
        CancellationTokenSource? session;
        lock (_lock)
        {
            session = _session;
            _session = null;
            _outgoing = null;
            _lastBraille = null;
        }
        session?.Dispose();
        var role = Role;
        Role = null;
        PeerName = null;
        SetState(RemoteState.Idle);
        Say(VoxStrings.Get(wasConnected ? "Remote.Ended" : role == RemoteRole.Controlled ? "Remote.StoppedWaiting" : "Remote.NotConnected"), SpeechPriority.High);
    }

    private void SetState(RemoteState state)
    {
        _state = state;
        StateChanged?.Invoke(this, state);
    }

    private void Say(string text, SpeechPriority priority) => _speech.Enqueue(new Utterance(text, priority));

    private static SpeechPriority ParsePriority(string? priority) =>
        Enum.TryParse<SpeechPriority>(priority, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : SpeechPriority.Normal;

    private static string Describe(Exception ex) => ex switch
    {
        RemoteAuthenticationException auth => auth.Message,
        Enterprise.NetworkDisabledException => VoxStrings.Get("Remote.NetworkOff"),
        TimeoutException timeout => timeout.Message,
        System.Net.Sockets.SocketException => VoxStrings.Get("Remote.Unreachable"),
        IOException io => io.Message,
        _ => VoxStrings.Get("Remote.Unreachable"),
    };

    public void Dispose()
    {
        Disconnect();
        Braille = null;
    }
}
