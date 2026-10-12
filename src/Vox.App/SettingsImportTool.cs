using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Lifecycle;
using Vox.Core.Speech;
using Vox.Core.Switching;

namespace Vox.App;

/// <summary>
/// <c>Vox.App --import-settings nvda|jaws [--user-profile &lt;path&gt;]</c> (docs/switching.md):
/// the import for unattended deployment, without starting the screen reader.
/// </summary>
internal static class SettingsImportTool
{
    public static int Run(string sourceName, string[] rest, RunPolicy policy)
    {
        if (policy.IsSecure || SettingsImport.ParseSource(sourceName) is not { } source
            || rest is not ([] or ["--user-profile", _]))
        {
            Console.Error.WriteLine(SettingsImportCli.Usage);
            return VoxExitCodes.Fatal;
        }
        var locations = rest is [_, var profile]
            ? ImportLocations.ForProfile(profile)
            : ImportLocations.ForCurrentUser(SettingsManager.DefaultUserSettingsPath);
        var configDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "config");
        var import = new SettingsImport(locations, VoicesOf, layout => KeyMap.LayoutBindings(configDirectory, layout));
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance,
            Path.Combine(configDirectory, "default-settings.json"), locations.SettingsPath);
        return SettingsImportCli.Run(import, source, settings.Load, settings.Save, Console.Out);
    }

    // The voices of each engine, asked of the engine directly (no audio is played)
    private static IReadOnlyList<string>? VoicesOf(string engine)
    {
        try
        {
            switch (engine)
            {
                case SpeechEngineRegistry.OneCoreId:
                {
                    using var synthesizer = new WinRtOneCoreSynthesizer();
                    return synthesizer.Voices.Select(v => v.Name).ToList();
                }
                case SpeechEngineRegistry.SapiId:
                {
                    using var sapi = new SapiSpeechEngine(NullLogger<SapiSpeechEngine>.Instance);
                    return sapi.GetAvailableVoices();
                }
                case SpeechEngineRegistry.EspeakId when EspeakComponent.IsInstalled(EspeakComponent.DefaultDirectory):
                {
                    var espeak = new EspeakSynthesizer(EspeakNative.Load(EspeakComponent.DefaultDirectory));
                    return espeak.Voices.Select(v => v.Name).ToList();
                }
                default:
                    return null;
            }
        }
        catch (Exception)
        {
            // An engine that can't start has no voices to match
            return null;
        }
    }
}
