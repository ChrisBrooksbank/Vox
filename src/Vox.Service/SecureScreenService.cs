using Vox.Core.Lifecycle;

namespace Vox.Service;

/// <summary>
/// Runs <see cref="SecureInstanceSupervisor"/> once a second: session changes (sign-in, fast user
/// switching, remote sessions taking the console) and crashed instances are picked up within a second.
/// </summary>
public sealed class SecureScreenService(ISecureInstanceHost host, ILogger<SecureScreenService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var supervisor = new SecureInstanceSupervisor(host, logger);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                supervisor.Reconcile();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error supervising the secure instance");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
