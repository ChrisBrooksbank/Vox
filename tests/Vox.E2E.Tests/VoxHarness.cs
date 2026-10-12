using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vox.App;
using Vox.Core.Configuration;
using Vox.Core.Lifecycle;
using Vox.Core.Speech;
using Vox.Core.Tests.TestSupport;

namespace Vox.E2E.Tests;

/// <summary>How a <see cref="VoxHarness"/> runs.</summary>
public sealed record VoxHarnessOptions
{
    /// <summary>Also say everything aloud through SAPI, to watch and hear a scenario (VOX_E2E_AUDIBLE=1).</summary>
    public bool Audible { get; init; }

    /// <summary>The options the environment asks for.</summary>
    public static VoxHarnessOptions FromEnvironment() =>
        new() { Audible = Environment.GetEnvironmentVariable("VOX_E2E_AUDIBLE") == "1" };
}

/// <summary>
/// Vox's own services, wired exactly as the app wires them (<see cref="ServiceRegistration"/>),
/// running in the test process with these changes: speech goes to a <see cref="RecordingSpeechEngine"/>
/// (and, when <see cref="VoxHarnessOptions.Audible"/>, also out loud), settings come from a
/// temporary read-only file (first-run wizard done, audio cues off), and the start-at-logon
/// registration is a no-op, so the user's own is never touched.
/// </summary>
public sealed class VoxHarness : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly string _settingsDirectory;

    public RecordingSpeechEngine Speech { get; } = new();

    /// <summary>Vox's services: fault-injection runs reach its seams (UIA thread, hook safety net) through them.</summary>
    public IServiceProvider Services => _host.Services;

    private VoxHarness(IHost host, string settingsDirectory, RecordingSpeechEngine speech)
    {
        _host = host;
        _settingsDirectory = settingsDirectory;
        Speech = speech;
    }

    public static async Task<VoxHarness> StartAsync(VoxHarnessOptions? options = null)
    {
        options ??= VoxHarnessOptions.FromEnvironment();
        var settingsDirectory = Path.Combine(Path.GetTempPath(), "vox-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(settingsDirectory);
        var settingsPath = Path.Combine(settingsDirectory, "settings.json");
        await File.WriteAllTextAsync(settingsPath, """{ "FirstRunCompleted": true, "AudioCuesEnabled": false }""");

        var speech = new RecordingSpeechEngine();
        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton(RunPolicy.Normal);
                ServiceRegistration.RegisterServices(context, services);
                services.Replace(ServiceDescriptor.Singleton(sp => new SettingsManager(
                    sp.GetRequiredService<ILogger<SettingsManager>>(),
                    Path.Combine(AppContext.BaseDirectory, "assets", "config", "default-settings.json"),
                    settingsPath) { ReadOnly = true }));
                // Never the user's own start-at-logon entry
                services.Replace(ServiceDescriptor.Singleton<IStartupRegistration, NoStartupRegistration>());
                services.Replace(ServiceDescriptor.Singleton(sp => new SpeechEngineRegistry(
                    [new SpeechEngineDescriptor("Recording", "Recording", () => options.Audible
                        ? new AudibleSpeechEngine(speech, new SapiSpeechEngine(sp.GetRequiredService<ILogger<SapiSpeechEngine>>()))
                        : speech)],
                    sp.GetRequiredService<ILogger<SpeechEngineRegistry>>())));
            })
            .Build();
        await host.StartAsync();
        return new VoxHarness(host, settingsDirectory, speech);
    }

    private sealed class NoStartupRegistration : IStartupRegistration
    {
        public string? Command => null;
        public void Register(string command) { }
        public void Unregister() { }
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync(TimeSpan.FromSeconds(10));
        _host.Dispose();
        try { Directory.Delete(_settingsDirectory, recursive: true); }
        catch (IOException) { }
    }
}
