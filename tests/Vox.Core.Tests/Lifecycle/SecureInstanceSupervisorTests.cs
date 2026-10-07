using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.Lifecycle;

public class SecureInstanceSupervisorTests
{
    private sealed class FakeInstance(int sessionId) : ISecureInstance
    {
        public int SessionId { get; } = sessionId;
        public bool HasExited { get; set; }
        public bool Stopped { get; private set; }
        public void Stop() { Stopped = true; HasExited = true; }
        public void Dispose() { }
    }

    private sealed class FakeHost : ISecureInstanceHost
    {
        public int? Session { get; set; } = 1;
        public List<FakeInstance> Started { get; } = new();
        public int? ActiveConsoleSession() => Session;
        public ISecureInstance Start(int sessionId)
        {
            var instance = new FakeInstance(sessionId);
            Started.Add(instance);
            return instance;
        }
    }

    private DateTimeOffset _now = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
    private readonly FakeHost _host = new();

    private SecureInstanceSupervisor Supervisor() => new(_host, NullLogger.Instance, () => _now);

    [Fact]
    public void Reconcile_StartsOneInstanceInTheConsoleSession()
    {
        var supervisor = Supervisor();

        supervisor.Reconcile();
        supervisor.Reconcile();

        var started = Assert.Single(_host.Started);
        Assert.Equal(1, started.SessionId);
    }

    [Fact]
    public void ConsoleSessionChanges_MovesTheInstance()
    {
        var supervisor = Supervisor();
        supervisor.Reconcile();

        _host.Session = 2; // fast user switching
        supervisor.Reconcile();

        Assert.True(_host.Started[0].Stopped);
        Assert.Equal(2, _host.Started[1].SessionId);
    }

    [Fact]
    public void NoConsoleSession_StopsAndStartsNothing()
    {
        var supervisor = Supervisor();
        supervisor.Reconcile();

        _host.Session = null;
        supervisor.Reconcile();

        Assert.True(_host.Started[0].Stopped);
        Assert.Single(_host.Started);
        Assert.Null(supervisor.Current);
    }

    [Fact]
    public void ExitedInstance_IsRestarted_UpToTheLimit()
    {
        var supervisor = Supervisor();
        for (int i = 0; i < 10; i++)
        {
            supervisor.Reconcile();
            _host.Started[^1].HasExited = true; // crashes straight away
        }

        Assert.Equal(SecureInstanceSupervisor.MaxRestartsPerMinute, _host.Started.Count);

        _now += TimeSpan.FromMinutes(1);
        supervisor.Reconcile();
        Assert.Equal(SecureInstanceSupervisor.MaxRestartsPerMinute + 1, _host.Started.Count);
    }

    [Fact]
    public void Dispose_StopsTheInstance()
    {
        var supervisor = Supervisor();
        supervisor.Reconcile();

        supervisor.Dispose();

        Assert.True(_host.Started[0].Stopped);
    }

    [Theory]
    [InlineData("Winlogon", true)]
    [InlineData("winlogon", true)]
    [InlineData("Default", false)]
    [InlineData(null, false)]
    public void ActiveOnlyOnTheWinlogonDesktop(string? desktop, bool active)
    {
        Assert.Equal(active, SecureDesktopActivation.ShouldBeActive(desktop));
    }
}
