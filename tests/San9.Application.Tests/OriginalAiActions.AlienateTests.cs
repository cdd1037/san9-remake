using San9.Content.Scenarios;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed partial class OriginalAiActionsTests
{
    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 9)]
    public void AlienatePlanningOrderCanPreferEitherEligibleTarget(bool preferLater, int expectedTarget)
    {
        var h = new Harness(draw: maximum => preferLater && maximum == 3 ? 0 : maximum - 1);

        Assert.Equal(1, h.Run(OriginalAiWork.Alienate).Submitted);

        var command = Assert.IsType<AlienateCommand>(Assert.Single(h.Commands));
        Assert.Equal(expectedTarget, command.TargetOfficerId);
        Assert.Contains(h.Engine.Snapshot.Simulation.Tasks, task => task.OfficerId == command.OfficerId &&
            task.Kind == "strategy-alienate");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void AlienateZeroOrOneCandidateDoesNotDrawForOrder(bool hasTarget, int expectedSubmissions)
    {
        var h = new Harness(change: world => world with
        {
            Officers = world.Officers.Select(p => p.OwnerId == 2 && (!hasTarget || p.Id != 4)
                ? p with { BaseAbilities = p.BaseAbilities with { Intelligence = 100 } } : p).ToArray()
        }, draw: _ => throw new InvalidOperationException("A single candidate needs no ordering draw."));

        Assert.Equal(expectedSubmissions, h.Run(OriginalAiWork.Alienate).Submitted);
        if (hasTarget)
            Assert.Equal(4, Assert.IsType<AlienateCommand>(Assert.Single(h.Commands)).TargetOfficerId);
        else
            Assert.Empty(h.Commands);
    }
}
