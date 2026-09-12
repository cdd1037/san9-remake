using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class DomesticCombinationOrderTests
{
    [Theory]
    [InlineData(11, 12, false)]
    [InlineData(2000, 3000, false)]
    [InlineData(int.MaxValue - 1, int.MaxValue, false)]
    [InlineData(int.MaxValue - 1, int.MaxValue, true)]
    public void IdentityCannotOvertakeAbilityOrRulerAndChangeCombinationDraws(int secondId, int thirdId, bool ruler)
    {
        var world = WorldState.Create(new(190, 1, 1), 7,
            [new(1, 10000, 10000, 100) { LeaderId = ruler ? 10 : 1 }],
            [new(1, 1, 0, 100, 100, 0, 500, 900, 1000, 500, 1000)],
            (ruler ? Array.Empty<OfficerState>() : [new OfficerState(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }])
                .Concat(new[]
                {
                    new OfficerState(10, 1, 1, new(50, 50, 80, 50), false) { Status = ruler ? OfficerStatus.Ruler : OfficerStatus.Serving },
                    new OfficerState(secondId, 1, 1, new(50, 50, ruler ? 250 : 79, 50), false),
                    new OfficerState(thirdId, 1, 1, new(50, 50, 78, 50), false)
                }), controls: [new("player", 1, ControllerKind.Player)]);
        var rules = new EconomyRulebook([], [new(DomesticActivity.Commerce, 10, thirdId, ruler, false),
            new(DomesticActivity.Commerce, secondId, thirdId, true, false)]);
        var engine = new SimulationEngine(world, rules);
        var participants = ImmutableArray.Create(thirdId, secondId, 10);
        int expectedBase = DomesticDevelopment.CalculateBaseEffect(DomesticActivity.Commerce, 900, 1000, 10000,
            participants.Select(id => engine.GetAbilities(engine.Snapshot)[id]).ToArray());
        var result = engine.Execute(new("player"), new DevelopCommand(Guid.NewGuid(), 1, 0, new(1, 1, participants, DomesticActivity.Commerce)));
        Assert.True(result.Submission.Accepted);
        // Independently known seed7 combination rolls: 8,74. Valid pair must get the first roll.
        Assert.Equal(2 * expectedBase, result.Development!.Value.Increment);
        var random = new StableRandom(StableRandom.DeriveSeed(7, "domestic-combination"));
        Assert.Equal(8UL, random.NextUInt64() % 100);
        Assert.Equal(random.State, engine.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "domestic-combination").State);
    }
}
