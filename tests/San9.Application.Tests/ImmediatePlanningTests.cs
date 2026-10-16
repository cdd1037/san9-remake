using System.Collections.Immutable;
using San9.Application.AI;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class ImmediatePlanningTests
{
    [Fact]
    public void TrainingIncludesHighIdSpecialistsAndValuesSaturationAndActorCost()
    {
        var world = GameSession.Start(G01ScenarioTests.Fixture(), 42).Snapshot;
        var abilities = Enumerable.Range(1, 6).ToImmutableDictionary(id => id,
            id => id == 6 ? new OfficerAbilities(10, 10, 10, 100) : new(90, 90, 90, 20));
        var home = new DomesticSettlementObservation(1, 1, 1000, 10000, 10000, 99, 1000, 100, 100, [1, 2, 3, 4, 5, 6]);
        var observation = new DomesticObservation(AiObservation.FromSnapshot(1, world), [home]) { Abilities = abilities, Trust = 100 };
        var one = new DevelopmentCommand(1, 1, [6], DomesticActivity.Train);
        var many = new DevelopmentCommand(1, 1, [1, 2, 3, 4, 6], DomesticActivity.Train);
        Assert.Contains(DomesticPlanner.Plan(observation, new(1000, 0), 0).Candidates,
            c => c.Activity == DomesticActivity.Train && c.OfficerIds.SequenceEqual([6]));
        Assert.True(DomesticPlanner.Value(one, observation) > DomesticPlanner.Value(many, observation));
    }

}
