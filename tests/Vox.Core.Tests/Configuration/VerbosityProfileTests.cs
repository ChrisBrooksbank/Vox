using Vox.Core.Configuration;
using Xunit;

namespace Vox.Core.Tests.Configuration;

public class VerbosityProfileTests
{
    [Fact]
    public void Beginner_AnnouncesEverything()
    {
        var profile = VerbosityProfile.Beginner;

        Assert.Equal(VerbosityLevel.Beginner, profile.Level);
        Assert.True(profile.AnnounceHeadingLevel);
        Assert.True(profile.AnnounceLandmarkType);
        Assert.True(profile.AnnounceControlType);
        Assert.True(profile.AnnounceVisitedState);
        Assert.True(profile.AnnounceRequiredState);
        Assert.True(profile.AnnounceExpandedState);
        Assert.True(profile.AnnouncePositionInfo);
        Assert.True(profile.AnnounceDescription);
    }

    [Fact]
    public void Intermediate_AnnouncesControlTypeAndEssentialState()
    {
        var profile = VerbosityProfile.Intermediate;

        Assert.Equal(VerbosityLevel.Intermediate, profile.Level);
        Assert.True(profile.AnnounceHeadingLevel);
        Assert.False(profile.AnnounceLandmarkType);
        Assert.True(profile.AnnounceControlType);
        Assert.True(profile.AnnounceVisitedState);
        Assert.True(profile.AnnounceRequiredState);
        Assert.True(profile.AnnounceExpandedState);
        Assert.False(profile.AnnouncePositionInfo);
        Assert.False(profile.AnnounceDescription);
    }

    [Fact]
    public void Advanced_AnnouncesMinimal()
    {
        var profile = VerbosityProfile.Advanced;

        Assert.Equal(VerbosityLevel.Advanced, profile.Level);
        Assert.False(profile.AnnounceHeadingLevel);
        Assert.False(profile.AnnounceLandmarkType);
        Assert.False(profile.AnnounceControlType);
        Assert.False(profile.AnnounceVisitedState);
        Assert.False(profile.AnnounceRequiredState);
        Assert.True(profile.AnnounceExpandedState);
        Assert.False(profile.AnnouncePositionInfo);
        Assert.False(profile.AnnounceDescription);
    }

    [Theory]
    [InlineData(VerbosityLevel.Beginner)]
    [InlineData(VerbosityLevel.Intermediate)]
    [InlineData(VerbosityLevel.Advanced)]
    public void For_ReturnsCorrectProfile(VerbosityLevel level)
    {
        var profile = VerbosityProfile.For(level);
        Assert.Equal(level, profile.Level);
    }

    [Fact]
    public void For_Beginner_ReturnsSameInstanceAsBeginner()
    {
        Assert.Same(VerbosityProfile.Beginner, VerbosityProfile.For(VerbosityLevel.Beginner));
    }

    [Fact]
    public void For_Intermediate_ReturnsSameInstanceAsIntermediate()
    {
        Assert.Same(VerbosityProfile.Intermediate, VerbosityProfile.For(VerbosityLevel.Intermediate));
    }

    [Fact]
    public void For_Advanced_ReturnsSameInstanceAsAdvanced()
    {
        Assert.Same(VerbosityProfile.Advanced, VerbosityProfile.For(VerbosityLevel.Advanced));
    }
}
