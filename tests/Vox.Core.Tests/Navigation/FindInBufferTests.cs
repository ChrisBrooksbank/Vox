using Vox.Core.Navigation;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class FindInBufferTests
{
    private const string Text = "Apples and pears\nMore apples\nPlums";

    [Fact]
    public void Forward_FindsTheFirstMatchAtOrAfterTheStart_IgnoringCase()
    {
        Assert.Equal(new FindMatch(0, false), FindInBuffer.Find(Text, "apples", 0, forward: true));
        Assert.Equal(new FindMatch(22, false), FindInBuffer.Find(Text, "APPLES", 1, forward: true));
    }

    [Fact]
    public void Forward_PastTheLastMatch_WrapsToTheTop()
    {
        Assert.Equal(new FindMatch(0, true), FindInBuffer.Find(Text, "apples", 23, forward: true));
    }

    [Fact]
    public void Backward_FindsTheLastMatchBeforeTheStart()
    {
        Assert.Equal(new FindMatch(0, false), FindInBuffer.Find(Text, "apples", 22, forward: false));
        // A match that starts before the start but runs past it counts
        Assert.Equal(new FindMatch(22, false), FindInBuffer.Find(Text, "apples", 23, forward: false));
    }

    [Fact]
    public void Backward_BeforeTheFirstMatch_WrapsToTheBottom()
    {
        Assert.Equal(new FindMatch(22, true), FindInBuffer.Find(Text, "apples", 0, forward: false));
    }

    [Fact]
    public void MatchCase_OnlyFindsTheSameCase()
    {
        Assert.Equal(new FindMatch(0, true), FindInBuffer.Find(Text, "Apples", 1, forward: true, matchCase: true));
        Assert.Null(FindInBuffer.Find(Text, "PLUMS", 0, forward: true, matchCase: true));
    }

    [Fact]
    public void FindsTextAcrossLines()
    {
        Assert.Equal(new FindMatch(11, false), FindInBuffer.Find(Text, "pears\nmore", 0, forward: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("kiwi")]
    [InlineData("a much longer search than the whole of the text")]
    public void NoMatch_IsNull(string query)
    {
        Assert.Null(FindInBuffer.Find(Text, query, 5, forward: true));
        Assert.Null(FindInBuffer.Find(Text, query, 5, forward: false));
    }

    [Fact]
    public void StartOutsideTheText_IsClamped()
    {
        Assert.Equal(new FindMatch(29, false), FindInBuffer.Find(Text, "plums", 1000, forward: false));
        Assert.Equal(new FindMatch(0, false), FindInBuffer.Find(Text, "apples", -5, forward: true));
    }

    [Fact]
    public void Remember_KeepsTheLastSearch_AndHistoryMostRecentFirstWithoutDuplicates()
    {
        var find = new FindInBuffer();
        Assert.Null(find.Last);

        find.Remember(new FindRequest("one"));
        find.Remember(new FindRequest("two", MatchCase: true));
        find.Remember(new FindRequest("one"));

        Assert.Equal(new FindRequest("one"), find.Last);
        Assert.Equal(new[] { "one", "two" }, find.History);
    }

    [Fact]
    public void History_IsCapped()
    {
        var find = new FindInBuffer();
        for (int i = 0; i < FindInBuffer.MaxHistory + 5; i++)
            find.Remember(new FindRequest($"search {i}"));

        Assert.Equal(FindInBuffer.MaxHistory, find.History.Count);
        Assert.Equal($"search {FindInBuffer.MaxHistory + 4}", find.History[0]);
    }
}
