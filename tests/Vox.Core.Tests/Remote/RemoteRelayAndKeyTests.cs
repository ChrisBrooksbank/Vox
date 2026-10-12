using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Configuration;
using Vox.Core.Lifecycle;
using Vox.Core.Remote;
using Xunit;

namespace Vox.Core.Tests.Remote;

public class RemoteRelayServerTests
{
    private const string Code = "ABCD-EFGH-JKMN";

    [Fact]
    public async Task TheRelay_PairsBothSidesOfASession_AndCarriesItEncrypted()
    {
        using var relay = new RemoteRelayServer(NullLogger.Instance);
        relay.Start(0, IPAddress.Loopback);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _ = relay.RunAsync(stop.Token);
        var transport = new RemoteTransport(RunPolicy.Normal);

        var waiting = transport.ConnectThroughRelayAsync("127.0.0.1", relay.Port, Code, RemoteRole.Controlled, stop.Token);
        await Task.Delay(200);
        var controllingStream = await transport.ConnectThroughRelayAsync("127.0.0.1", relay.Port, Code, RemoteRole.Controlling, stop.Token);
        var controlledStream = await waiting;
        var controlled = SecureChannel.EstablishAsync(controlledStream, RemoteRole.Controlled, Code, stop.Token);
        await using var controlling = await SecureChannel.EstablishAsync(controllingStream, RemoteRole.Controlling, Code, stop.Token);
        await using var controlledChannel = await controlled;

        await controlling.SendAsync("hello"u8.ToArray(), stop.Token);

        Assert.Equal("hello"u8.ToArray(), await controlledChannel.ReceiveAsync(stop.Token));
    }

    [Fact]
    public async Task ASecondComputerOnTheSameSide_IsTurnedAway()
    {
        using var relay = new RemoteRelayServer(NullLogger.Instance);
        relay.Start(0, IPAddress.Loopback);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _ = relay.RunAsync(stop.Token);
        var transport = new RemoteTransport(RunPolicy.Normal);

        _ = transport.ConnectThroughRelayAsync("127.0.0.1", relay.Port, Code, RemoteRole.Controlled, stop.Token);
        await Task.Delay(200);
        var error = await Assert.ThrowsAsync<IOException>(() =>
            transport.ConnectThroughRelayAsync("127.0.0.1", relay.Port, Code, RemoteRole.Controlled, stop.Token));

        Assert.Contains("already", error.Message);
    }

    [Fact]
    public void TheRequest_CarriesOnlyTheChannel() =>
        Assert.Equal($"VOXRELAY 1 {RemoteCode.RelayChannel(Code)} controlled\n", RemoteRelayServer.Request(RemoteCode.RelayChannel(Code), RemoteRole.Controlled));

    [Theory]
    [InlineData("pc.example.com", "pc.example.com", 6837)]
    [InlineData("pc.example.com:7000", "pc.example.com", 7000)]
    [InlineData("192.168.1.5", "192.168.1.5", 6837)]
    [InlineData("[::1]:9000", "::1", 9000)]
    public void Addresses_AreRead(string text, string host, int port) =>
        Assert.Equal((host, port), RemoteEndpoint.ParseAddress(text, 6837));

    [Theory]
    [InlineData("")]
    [InlineData("http://x/y")]
    [InlineData("a b")]
    public void NonAddresses_AreNot(string text) => Assert.Null(RemoteEndpoint.ParseAddress(text, 6837));
}

public class RemoteKeyPlayerTests
{
    private sealed class RecordingInjector : IRemoteKeyInjector
    {
        public List<(int Vk, int Scan, bool Down, bool Extended)> Sent { get; } = [];
        public void SendRaw(int vkCode, int scanCode, bool down, bool extended) => Sent.Add((vkCode, scanCode, down, extended));
    }

    private static RemoteMessage Key(int vk, bool down, bool voxKey = false, bool keypad = false, int scan = 30) =>
        new() { Type = RemoteMessage.Key, Vk = vk, Down = down, VoxKey = voxKey, Keypad = keypad, Scan = scan };

    [Fact]
    public void Keys_ArePressedAndReleased_AsSent()
    {
        var injector = new RecordingInjector();
        var player = new RemoteKeyPlayer(injector, () => ModifierKey.Insert);

        player.Play(Key(0x41, true));
        player.Play(Key(0x41, false));

        Assert.Equal([(0x41, 30, true, false), (0x41, 30, false, false)], injector.Sent);
    }

    [Theory]
    [InlineData(ModifierKey.Insert, 0x2D)]
    [InlineData(ModifierKey.CapsLock, 0x14)]
    public void TheScreenReaderKey_IsThisComputersOwn(ModifierKey modifier, int expected)
    {
        var injector = new RecordingInjector();
        var player = new RemoteKeyPlayer(injector, () => modifier);

        player.Play(Key(0x14, true, voxKey: true));

        Assert.Equal(expected, injector.Sent.Single().Vk);
    }

    [Fact]
    public void AKeyUpOfAKeyNeverPressed_IsIgnored()
    {
        var injector = new RecordingInjector();
        var player = new RemoteKeyPlayer(injector, () => ModifierKey.Insert);

        player.Play(Key(0x41, false));

        Assert.Empty(injector.Sent);
    }

    [Fact]
    public void ReleaseAll_LetsGoOfEveryHeldKey()
    {
        var injector = new RecordingInjector();
        var player = new RemoteKeyPlayer(injector, () => ModifierKey.Insert);
        player.Play(Key(0xA2, true));
        player.Play(Key(0x2D, true, voxKey: true));

        player.ReleaseAll();

        Assert.Equal(0, player.HeldCount);
        Assert.Contains((0xA2, 0, false, false), injector.Sent);
        Assert.Contains(injector.Sent, k => k.Vk == 0x2D && !k.Down);
    }

    [Fact]
    public void NavigationKeys_AreExtended_ButNotOnTheKeypad()
    {
        var injector = new RecordingInjector();
        var player = new RemoteKeyPlayer(injector, () => ModifierKey.Insert);

        player.Play(Key(0x26, true));
        player.Play(Key(0x28, true, keypad: true));
        player.Play(Key(0x00, true));

        Assert.Equal([(0x26, 30, true, true), (0x28, 30, true, false)], injector.Sent);
    }

    [Fact]
    public void Messages_RoundTrip()
    {
        var message = new RemoteMessage { Type = RemoteMessage.Speak, Text = "Hello", Priority = "Interrupt" };

        Assert.Equal(message, RemoteMessage.FromBytes(message.ToBytes()));
        Assert.Null(RemoteMessage.FromBytes("not json"u8.ToArray()));
        Assert.Null(RemoteMessage.FromBytes("{}"u8.ToArray()));
    }
}
