using Vox.Core.Configuration;
using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.Lifecycle;

public class RemoteSessionTests
{
    private sealed class FakeProbe : IRemoteSessionProbe
    {
        public bool IsRemoteSession { get; set; }
        public int? ClientProtocolType { get; set; }
        public string? SessionName { get; set; }
        public string? ClientName { get; set; }
        public bool Throws { get; set; }

        bool IRemoteSessionProbe.IsRemoteSession => Throws ? throw new InvalidOperationException("no WTS") : IsRemoteSession;
    }

    [Fact]
    public void TheConsole_IsLocal()
    {
        var session = RemoteSessionDetector.Detect(new FakeProbe { ClientProtocolType = 0, SessionName = "Console", ClientName = "" });

        Assert.Same(RemoteSession.Local, session);
        Assert.False(session.IsRemote);
        Assert.Null(session.Announcement);
        Assert.Equal("Local session", session.Describe());
    }

    [Fact]
    public void Rdp_IsRemoteDesktop_FromTheProtocol()
    {
        var session = RemoteSessionDetector.Detect(new FakeProbe { IsRemoteSession = true, ClientProtocolType = 2, SessionName = "RDP-Tcp#3", ClientName = "LAPTOP-7 " });

        Assert.Equal(RemoteSessionKind.RemoteDesktop, session.Kind);
        Assert.Equal("Remote session: Remote Desktop", session.Announcement);
        Assert.Equal("Remote session: Remote Desktop from LAPTOP-7", session.Describe());
    }

    [Fact]
    public void Citrix_IsFoundFromTheProtocol_OrTheSessionName()
    {
        Assert.Equal(RemoteSessionKind.Citrix,
            RemoteSessionDetector.Detect(new FakeProbe { IsRemoteSession = true, ClientProtocolType = 1 }).Kind);
        Assert.Equal(RemoteSessionKind.Citrix,
            RemoteSessionDetector.Detect(new FakeProbe { IsRemoteSession = true, SessionName = "ICA-CGP#2" }).Kind);
        Assert.Equal("Remote session: Citrix",
            RemoteSessionDetector.Detect(new FakeProbe { ClientProtocolType = 1 }).Announcement);
    }

    [Fact]
    public void TheSessionName_Decides_WhenTheProtocolIsUnknown()
    {
        Assert.Equal(RemoteSessionKind.RemoteDesktop,
            RemoteSessionDetector.Detect(new FakeProbe { SessionName = "RDP-Tcp#0" }).Kind);
    }

    [Fact]
    public void ARemoteSession_OfAnUnknownKind_IsOther()
    {
        var session = RemoteSessionDetector.Detect(new FakeProbe { IsRemoteSession = true, ClientProtocolType = 0, SessionName = "Console" });

        Assert.Equal(RemoteSessionKind.Other, session.Kind);
        Assert.Equal("Remote session: remote connection", session.Announcement);
    }

    [Fact]
    public void AProbeThatFails_CountsAsLocal()
    {
        Assert.Same(RemoteSession.Local, RemoteSessionDetector.Detect(new FakeProbe { Throws = true }));
    }

    [Fact]
    public void Remotely_TheAudioDeviceIsNotKeptAwake()
    {
        var settings = new VoxSettings { KeepAudioDeviceAwake = true, AudioDucking = AudioDuckingMode.WhileSpeaking };

        var remote = new RemoteSession(RemoteSessionKind.RemoteDesktop).Adjust(settings);

        Assert.False(remote.KeepAudioDeviceAwake);
        Assert.Equal(AudioDuckingMode.WhileSpeaking, remote.AudioDucking);
        Assert.Same(settings, RemoteSession.Local.Adjust(settings));
    }

    [Fact]
    public void TheMonitor_NoticesASessionTurningRemote_Once()
    {
        var probe = new FakeProbe { ClientProtocolType = 0, SessionName = "Console" };
        var monitor = new RemoteSessionMonitor(probe);
        var changes = new List<RemoteSession>();
        monitor.Changed += (_, s) => changes.Add(s);

        Assert.False(monitor.Refresh());
        probe.ClientProtocolType = 2;
        Assert.True(monitor.Refresh());
        Assert.False(monitor.Refresh());
        probe.ClientProtocolType = 0;
        Assert.True(monitor.Refresh());

        Assert.Equal([RemoteSessionKind.RemoteDesktop, RemoteSessionKind.Local], changes.Select(c => c.Kind));
        Assert.False(monitor.Current.IsRemote);
    }

    [Fact]
    public void TheWindowsProbe_AnswersWithoutThrowing()
    {
        var probe = new Win32RemoteSessionProbe();

        // Wherever the tests run: a real answer on Windows, nothing elsewhere
        _ = RemoteSessionDetector.Detect(probe);
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(probe.IsRemoteSession);
            Assert.Null(probe.ClientProtocolType);
        }
    }
}
