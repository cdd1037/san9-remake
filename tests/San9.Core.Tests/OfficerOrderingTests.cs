using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class OfficerOrderingTests
{
    [Fact]
    public void TacticGrowthIgnoresCollectionAndParticipantPermutation()
    {
        (WorldSnapshot State, string Events) Resolve(bool reverse)
        {
            int[] ids = reverse ? [40, 30, 20, 10] : [10, 20, 30, 40];
            var world = WorldState.Create(new(190, 1, 1), 42, [new(1, 0)],
                [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
                ids.Select(id => new OfficerState(id, 1, 1, new(99, 99, 99, 99), false)), corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 1000)) { IsRoot = true }]);
            var mutation = world.BeginMutation();
            mutation.SetSimulation(mutation.Simulation with { RandomStreams =
                [new("military-tactic-growth", 52), new("military-tactic-growth-member", 52), new("military-tactic-learning", 52)] });
            var members = ids.Select(id => mutation.Officers[id]).ToArray();
            List<DomainEvent> events = [];
            TacticCombat.Grow(mutation, members, reverse ? [40, 10] : [10, 40],
                new(5000, 0, TacticEffect.Martial, 1, 1, 0, true, false, []), events);
            Assert.Equal(10, Assert.Single(events.OfType<OfficerAbilityGrownEvent>()).OfficerId);
            Assert.Equal(new[] { 10, 20, 30, 40 }, events.OfType<TacticProficiencyGrownEvent>().Select(e => e.OfficerId));
            Assert.Equal(10, events.OfType<TacticLearnedEvent>().First().OfficerId);
            world.Commit(mutation);
            return (world.CreateSnapshot(), CanonicalJson.Text(events));
        }

        var forward = Resolve(false);
        var reversed = Resolve(true);
        Assert.Equal(forward.Events, reversed.Events);
        Assert.Equal(forward.State.StateHash, reversed.State.StateHash);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("other-unit")]
    [InlineData("captive")]
    public void CommanderMustReferenceACurrentServingMember(string invalid)
    {
        var person = new OfficerState(10, 1, invalid == "other-unit" ? 1 : null, new(50, 50, 50, 50), false)
        { UnitId = invalid == "other-unit" ? null : 1, Status = invalid == "captive" ? OfficerStatus.Captive : OfficerStatus.Serving };
        Assert.Throws<ArgumentException>(() => WorldState.Create(new(190, 1, 1), 42,
            [new(1, 0)], [new(1, 1, 100, 100, 100, 0, 0, 0, 100, 0, 100)], [person],
            units: [new(1, 1, new(2, 2), 100) { FormationId = 1, CommanderId = invalid == "missing" ? 99 : 10 }], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 1000)) { IsRoot = true }]));
    }
}
