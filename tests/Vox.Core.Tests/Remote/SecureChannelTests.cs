using System.Security.Cryptography;
using System.Text;
using Vox.Core.Remote;
using Xunit;

namespace Vox.Core.Tests.Remote;

public class SecureChannelTests
{
    private const string Code = "ABCD-EFGH-JKMN";

    private static async Task<(SecureChannel Controlled, SecureChannel Controlling, TapStream Tap)> ConnectAsync(
        string controlledCode = Code, string controllingCode = Code)
    {
        var (a, b) = await RemoteTestSupport.ConnectedPairAsync();
        var tap = new TapStream(b);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var controlled = SecureChannel.EstablishAsync(a, RemoteRole.Controlled, controlledCode, timeout.Token);
        var controlling = SecureChannel.EstablishAsync(tap, RemoteRole.Controlling, controllingCode, timeout.Token);
        return (await controlled, await controlling, tap);
    }

    [Fact]
    public async Task BothSidesWithTheCode_TalkBothWays()
    {
        var (controlled, controlling, _) = await ConnectAsync(controllingCode: "abcd efgh jkmn");
        await using var _1 = controlled;
        await using var _2 = controlling;

        await controlling.SendAsync("keys"u8.ToArray());
        await controlled.SendAsync("speech"u8.ToArray());

        Assert.Equal("keys", Encoding.UTF8.GetString((await controlled.ReceiveAsync())!));
        Assert.Equal("speech", Encoding.UTF8.GetString((await controlling.ReceiveAsync())!));
    }

    [Fact]
    public async Task TheWrongCode_IsRefusedOnBothSides()
    {
        var (a, b) = await RemoteTestSupport.ConnectedPairAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var controlled = SecureChannel.EstablishAsync(a, RemoteRole.Controlled, Code, timeout.Token);
        var controlling = SecureChannel.EstablishAsync(b, RemoteRole.Controlling, "ABCD-EFGH-JKMP", timeout.Token);

        await Assert.ThrowsAsync<RemoteAuthenticationException>(() => controlled);
        await Assert.ThrowsAnyAsync<Exception>(() => controlling);
    }

    [Fact]
    public async Task TwoComputersOnTheSameSide_AreRefused()
    {
        var (a, b) = await RemoteTestSupport.ConnectedPairAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var one = SecureChannel.EstablishAsync(a, RemoteRole.Controlled, Code, timeout.Token);
        var two = SecureChannel.EstablishAsync(b, RemoteRole.Controlled, Code, timeout.Token);

        var error = await Assert.ThrowsAsync<RemoteAuthenticationException>(() => one);
        Assert.Contains("same side", error.Message);
        await Assert.ThrowsAsync<RemoteAuthenticationException>(() => two);
    }

    [Fact]
    public async Task AChangedFrame_IsRejected()
    {
        var (controlled, controlling, tap) = await ConnectAsync();
        await using var _1 = controlled;
        await using var _2 = controlling;

        tap.ChangeNextWrite = frame =>
        {
            frame[^1] ^= 0x01;
            return frame;
        };
        await controlling.SendAsync("press Enter"u8.ToArray());

        await Assert.ThrowsAnyAsync<CryptographicException>(() => controlled.ReceiveAsync());
    }

    [Fact]
    public async Task AReplayedFrame_IsRejected()
    {
        var (controlled, controlling, tap) = await ConnectAsync();
        await using var _1 = controlled;
        await using var _2 = controlling;

        await controlling.SendAsync("press Enter"u8.ToArray());
        Assert.NotNull(await controlled.ReceiveAsync());
        // The same frame again, sent straight onto the connection
        await tap.Inner.WriteAsync(tap.Writes[^1]);

        await Assert.ThrowsAnyAsync<CryptographicException>(() => controlled.ReceiveAsync());
    }

    [Fact]
    public async Task SomethingThatIsntVox_IsRefused()
    {
        var (a, b) = await RemoteTestSupport.ConnectedPairAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var controlled = SecureChannel.EstablishAsync(a, RemoteRole.Controlled, Code, timeout.Token);

        await b.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: x\r\n\r\n" + new string(' ', 100)));

        await Assert.ThrowsAsync<RemoteAuthenticationException>(() => controlled);
    }

    [Fact]
    public async Task AClosedConnection_EndsReceiving()
    {
        var (controlled, controlling, _) = await ConnectAsync();
        await using var _1 = controlled;

        await controlling.DisposeAsync();

        Assert.Null(await controlled.ReceiveAsync());
    }
}
