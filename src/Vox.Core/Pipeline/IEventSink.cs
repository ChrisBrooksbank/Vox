namespace Vox.Core.Pipeline;

/// <summary>
/// Minimal interface for posting events to the event pipeline.
/// Implemented by EventPipeline and by test fakes.
/// </summary>
public interface IEventSink
{
    void Post(ScreenReaderEvent evt);
}
