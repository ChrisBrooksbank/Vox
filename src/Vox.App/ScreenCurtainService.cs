using Microsoft.Extensions.Hosting;
using Vox.Core.ScreenCurtain;

namespace Vox.App;

/// <summary>
/// Turns the screen curtain on at start-up when the setting says (after <see cref="ScreenReaderService"/>,
/// so the warning is said once speech is up) and takes it off when Vox stops.
/// </summary>
public sealed class ScreenCurtainService(ScreenCurtainCommands curtain) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        curtain.StartIfWanted();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        curtain.Dispose();
        return Task.CompletedTask;
    }
}
