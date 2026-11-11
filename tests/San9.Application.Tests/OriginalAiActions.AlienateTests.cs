using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed partial class OriginalAiActionsTests
{
    [Theory]
    [InlineData(59, 59, 59, 59, 0)]
    [InlineData(60, 59, 59, 59, 1)]
    [InlineData(59, 60, 59, 59, 1)]
    [InlineData(59, 59, 60, 59, 1)]
    [InlineData(59, 59, 59, 60, 1)]
    [InlineData(59, 91, 59, 59, 0)]
    public void AlienateRequiresAtLeastOneEffectiveAbilityOfSixty(int war, int intelligence, int politics, int leadership, int submissions)
    {
        var h = new Harness(change: world => world with
        {
            Officers = world.Officers.Select(p => p.Id == 4
                ? p with { BaseAbilities = new(war, intelligence, politics, leadership) }
                : p.OwnerId == 2 ? p with { BaseAbilities = p.BaseAbilities with { Intelligence = 100 } } : p).ToArray()
        }, draw: _ => throw new InvalidOperationException("Filtered and single-target lists need no ordering draws."));
        Assert.Equal(submissions, h.Run(OriginalAiWork.Alienate).Submitted);
        if (submissions > 0) Assert.Equal(4, Assert.IsType<AlienateCommand>(Assert.Single(h.Commands)).TargetOfficerId);
        else Assert.Empty(h.Commands);
    }

    [Theory]
    [InlineData(59, 0, 0, 0)]
    [InlineData(60, 0, 0, 1)]
    [InlineData(60, 1, 0, 0)]
    [InlineData(59, 0, 1, 1)]
    [InlineData(66, 1, 1, 1)]
    public void AlienateMinimumAbilityUsesInjuryAndTreasureBonuses(int war, int injury, int bonus, int submissions)
    {
        var basis = Fixture();
        var content = basis with { Information = basis.Information with
        {
            Treasures = [new(700, "item", 1, 0, null) { AbilityBonuses = new(bonus, 0, 0, 0) }],
            TreasureStates = [new(700, 4, TreasureDiscovery.Discovered, null)]
        } };
        var h = new Harness(content, change: world => world with
        {
            Officers = world.Officers.Select(p => p.Id == 4
                ? p with { BaseAbilities = new(war, 30, 30, 30), Injury = injury }
                : p.OwnerId == 2 ? p with { BaseAbilities = p.BaseAbilities with { Intelligence = 100 } } : p).ToArray()
        }, draw: _ => throw new InvalidOperationException("A single target needs no ordering draw."));
        Assert.Equal(submissions, h.Run(OriginalAiWork.Alienate).Submitted);
    }

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
