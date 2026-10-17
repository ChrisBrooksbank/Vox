using Vox.Core.Lifecycle;

namespace Vox.Service;

/// <summary>Receives the settings users send for the secure screens (the folder is closed to them).</summary>
public sealed class SecureSettingsService(SecureSettingsReceiver receiver) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        SecureSettingsPipe.ServeAsync(receiver, stoppingToken);
}
