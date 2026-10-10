using Vox.Core.Tests.TestSupport;

namespace Vox.E2E.Tests;

public static class SpeechWaits
{
    /// <summary>
    /// Waits until nothing new has been said for <paramref name="quiet"/> (Vox has finished
    /// responding to a key), or <paramref name="maxWait"/> has passed.
    /// </summary>
    public static async Task QuietAsync(RecordingSpeechEngine speech, TimeSpan quiet, TimeSpan maxWait)
    {
        var deadline = DateTime.UtcNow + maxWait;
        int count = speech.Spoken.Count;
        var lastChange = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            int now = speech.Spoken.Count;
            if (now != count)
            {
                count = now;
                lastChange = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - lastChange >= quiet)
            {
                return;
            }
        }
    }
}
