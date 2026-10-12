using Vox.Core.Pipeline;

namespace Vox.Core.Navigation;

/// <summary>
/// Shows one of Vox's own dialogs from any thread: browse keys are turned off on the pipeline
/// thread first (<see cref="BrowseModeController.BeginOwnDialog"/>) and back on when it closes.
/// When another Vox dialog is open already, nothing is shown.
/// </summary>
public sealed class OwnDialogRunner(BrowseModeController browse, IEventSink pipeline)
{
    private static readonly TimeSpan PipelineTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs <paramref name="show"/> and returns its result, or <paramref name="whenBusy"/> when
    /// another Vox dialog is open.
    /// </summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> show, T whenBusy)
    {
        var begun = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        // BeginOwnDialog is pipeline-thread only
        pipeline.Post(new PipelineWorkEvent(DateTimeOffset.UtcNow, () => begun.TrySetResult(browse.BeginOwnDialog())));
        try
        {
            if (!await begun.Task.WaitAsync(PipelineTimeout).ConfigureAwait(false))
                return whenBusy;
        }
        catch (TimeoutException)
        {
            // The pipeline is stopping (or stuck): if the dialog begins after all, end it again
            _ = begun.Task.ContinueWith(t =>
            {
                if (t.Result)
                    pipeline.Post(new VoxDialogClosedEvent(DateTimeOffset.UtcNow));
            }, TaskScheduler.Default);
            return whenBusy;
        }
        try
        {
            return await show().ConfigureAwait(false);
        }
        finally
        {
            pipeline.Post(new VoxDialogClosedEvent(DateTimeOffset.UtcNow));
        }
    }
}
