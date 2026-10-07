using Vox.Core.Lifecycle;
using Xunit;

namespace Vox.Core.Tests.Lifecycle;

public class StartAtLogonTests
{
    private sealed class FakeRegistration : IStartupRegistration
    {
        public string? Command { get; set; }
        public void Register(string command) => Command = command;
        public void Unregister() => Command = null;
    }

    [Fact]
    public void CommandFor_PrefersTheWatchdog()
    {
        var dir = Path.Combine("C:", "Program Files", "Vox");

        Assert.Equal($"\"{Path.Combine(dir, "Vox.Watchdog.exe")}\"", StartAtLogon.CommandFor(dir, _ => true));
        Assert.Equal($"\"{Path.Combine(dir, "Vox.App.exe")}\"", StartAtLogon.CommandFor(dir, _ => false));
    }

    [Fact]
    public void Apply_RegistersAndUnregisters()
    {
        var registration = new FakeRegistration();

        Assert.True(StartAtLogon.Apply(true, registration, "\"vox.exe\"", RunPolicy.Normal));
        Assert.Equal("\"vox.exe\"", registration.Command);
        Assert.False(StartAtLogon.Apply(true, registration, "\"vox.exe\"", RunPolicy.Normal)); // already so

        Assert.True(StartAtLogon.Apply(false, registration, "\"vox.exe\"", RunPolicy.Normal));
        Assert.Null(registration.Command);
    }

    [Fact]
    public void Apply_MovedInstall_UpdatesTheCommand()
    {
        var registration = new FakeRegistration { Command = "\"old.exe\"" };

        Assert.True(StartAtLogon.Apply(true, registration, "\"new.exe\"", RunPolicy.Normal));
        Assert.Equal("\"new.exe\"", registration.Command);
    }

    [Fact]
    public void Apply_InSecureMode_DoesNothing()
    {
        var registration = new FakeRegistration { Command = "\"vox.exe\"" };

        Assert.False(StartAtLogon.Apply(false, registration, "\"vox.exe\"", RunPolicy.Secure));
        Assert.Equal("\"vox.exe\"", registration.Command);
    }
}
