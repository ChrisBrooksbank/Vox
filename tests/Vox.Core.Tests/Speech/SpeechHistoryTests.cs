using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class SpeechHistoryTests
{
    [Fact]
    public void KeepsTheMostRecentEntries()
    {
        var history = new SpeechHistory(capacity: 3);

        foreach (var text in new[] { "one", "two", "three", "four" })
            history.Add(text);

        Assert.Equal(["two", "three", "four"], history.Entries);
    }

    [Fact]
    public void BlankText_IsNotRecorded()
    {
        var history = new SpeechHistory();

        history.Add("  ");

        Assert.Empty(history.Entries);
    }

    [Fact]
    public void Added_IsRaisedForEachEntry()
    {
        var history = new SpeechHistory();
        var added = new List<string>();
        history.Added += (_, text) => added.Add(text);

        history.Add("hello");

        Assert.Equal(["hello"], added);
    }
}
