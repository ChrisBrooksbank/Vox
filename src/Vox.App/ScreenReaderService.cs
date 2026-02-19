using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.App;

/// <summary>
/// Main hosted service for the Vox screen reader.
/// Initializes all subsystems and manages application lifecycle.
/// </summary>
public sealed class ScreenReaderService : IHostedService
{
    private readonly ISpeechEngine _speechEngine;
    private readonly SpeechQueue _speechQueue;
    private readonly EventPipeline _eventPipeline;
    private readonly ILogger<ScreenReaderService> _logger;

    public ScreenReaderService(
        ISpeechEngine speechEngine,
        SpeechQueue speechQueue,
        EventPipeline eventPipeline,
        ILogger<ScreenReaderService> logger)
    {
        _speechEngine = speechEngine;
        _speechQueue = speechQueue;
        _eventPipeline = eventPipeline;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Vox Screen Reader starting");

        // Announce startup
        _speechQueue.Enqueue(new Utterance("Vox screen reader started", SpeechPriority.Normal));

        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Vox Screen Reader stopping");

        _speechEngine.Cancel();

        await Task.CompletedTask;
    }
}
