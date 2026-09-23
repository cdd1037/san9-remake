using San9.Application.Profiles;

namespace San9.Application.Tests;

public sealed class PlayerProfileTests
{
    [Fact]
    public void Completion_IsIdempotentAndPreservesEarlierSnapshots()
    {
        var first = PlayerProfile.Empty.Complete("test:lesson");
        var second = first.Complete("test:victory");
        Assert.Empty(PlayerProfile.Empty.CompletedMilestones);
        Assert.Single(first.CompletedMilestones);
        Assert.Equal(2, second.CompletedMilestones.Count);
        Assert.Same(first, first.Complete("test:lesson"));
        Assert.DoesNotContain("TEST:LESSON", first.CompletedMilestones);
    }

    [Fact]
    public void Constructor_CopiesInputAndCollapsesDuplicateFacts()
    {
        string[] input = ["test:lesson", "test:lesson"];
        var profile = new PlayerProfile(input);
        input[0] = "test:changed";
        Assert.Equal("test:lesson", Assert.Single(profile.CompletedMilestones));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void InvalidIdentifiers_AreRejected(string? id)
    {
        Assert.ThrowsAny<ArgumentException>(() => PlayerProfile.Empty.Complete(id!));
        Assert.ThrowsAny<ArgumentException>(() => new PlayerProfile([id!]));
    }
}
