using Vox.Core.Tests.TestSupport;
using Vox.Core.AddOns;
using Vox.Sdk;

namespace Vox.Core.Tests.AddOns;

public class AddOnSpeechLimiterTests
{
    [Theory]
    [InlineData(SpeechPriority.Normal)]
    [InlineData(SpeechPriority.Low)]
    public void NonUrgentSpeech_IsNeverLimited(SpeechPriority priority)
    {
        var limiter = new AddOnSpeechLimiter(new ManualTimeProvider());
        for (var i = 0; i < 50; i++)
            Assert.Equal(priority, limiter.Limit(priority));
        Assert.False(limiter.HasLimited);
    }

    [Theory]
    [InlineData(SpeechPriority.Interrupt)]
    [InlineData(SpeechPriority.High)]
    public void UrgentSpeech_PastTheLimit_BecomesNormal(SpeechPriority priority)
    {
        var limiter = new AddOnSpeechLimiter(new ManualTimeProvider());
        for (var i = 0; i < AddOnSpeechLimiter.MaxUrgent; i++)
            Assert.Equal(priority, limiter.Limit(priority));

        Assert.Equal(SpeechPriority.Normal, limiter.Limit(priority));
        Assert.True(limiter.HasLimited);
    }

    [Fact]
    public void InterruptAndHigh_ShareTheLimit()
    {
        var limiter = new AddOnSpeechLimiter(new ManualTimeProvider());
        for (var i = 0; i < AddOnSpeechLimiter.MaxUrgent; i++)
            limiter.Limit(i % 2 == 0 ? SpeechPriority.Interrupt : SpeechPriority.High);

        Assert.Equal(SpeechPriority.Normal, limiter.Limit(SpeechPriority.Interrupt));
    }

    [Fact]
    public void UrgentSpeech_IsAllowedAgain_AfterTheWindow()
    {
        var time = new ManualTimeProvider();
        var limiter = new AddOnSpeechLimiter(time);
        for (var i = 0; i < AddOnSpeechLimiter.MaxUrgent + 2; i++)
            limiter.Limit(SpeechPriority.High);

        time.Advance(AddOnSpeechLimiter.Window);

        Assert.Equal(SpeechPriority.High, limiter.Limit(SpeechPriority.High));
    }

    [Fact]
    public void LimitedSpeech_DoesNotExtendTheWindow()
    {
        var time = new ManualTimeProvider();
        var limiter = new AddOnSpeechLimiter(time);
        for (var i = 0; i < AddOnSpeechLimiter.MaxUrgent; i++)
            limiter.Limit(SpeechPriority.High);
        time.Advance(AddOnSpeechLimiter.Window - TimeSpan.FromMilliseconds(1));
        Assert.Equal(SpeechPriority.Normal, limiter.Limit(SpeechPriority.High));

        time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(SpeechPriority.High, limiter.Limit(SpeechPriority.High));
    }
}
