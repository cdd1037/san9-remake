using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SalaryCycleTests
{
    // Independent boundary inputs: paid chance 7 at trust1000/distance0,
    // 5 at trust1000/distance50, and 0 at trust0/distance50.
    [Theory]
    [InlineData(true, 1000, 0, 0, 6, 90, 91)]
    [InlineData(true, 1000, 0, 0, 7, 90, 90)]
    [InlineData(true, 1000, 75, 0, 4, 90, 91)]
    [InlineData(true, 1000, 75, 0, 5, 90, 90)]
    [InlineData(true, 0, 50, 0, 0, 90, 90)]
    [InlineData(true, 0, 149, 0, 1, 99, 100)]
    [InlineData(true, 1000, 0, 0, 0, 100, 101)]
    [InlineData(true, 1000, 0, 0, 0, 255, 255)]
    [InlineData(false, 1000, 0, 0, 79, 90, 89)]
    [InlineData(false, 1000, 0, 0, 80, 90, 90)]
    [InlineData(false, 0, 75, 15, 4, 90, 89)]
    [InlineData(false, 0, 75, 15, 5, 90, 90)]
    [InlineData(false, 0, 0, 0, 0, 0, 0)]
    public void SalaryFeedbackPreservesProbabilityBoundariesAndConsumesOneDraw(
        bool paid, int trust, int affinity, int righteousness, int roll, int loyalty, int expected)
    {
        var world = WorldState.Create(new(190, 1, 1), 190,
            [new(1, trust) { LeaderId = 1 }, new(2, 0)],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 1, new(50, 50, 50, 50), false) { OfficeId = 1, Affinity = affinity, Righteousness = righteousness,
                 ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, loyalty).Add(2, 123) }], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(paid ? 10u : 9u, 0)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(0, 0)) { IsRoot = true }]);
        var mutation = world.BeginMutation();
        ulong state = 0;
        while (new StableRandom(state).NextUInt64() % 100 != (ulong)roll) state++;
        var random = new StableRandom(state);
        random.NextUInt64();
        mutation.SetSimulation(mutation.Simulation with { RandomStreams = [new("personnel-salary-loyalty", state)] });
        List<DomainEvent> events = [];
        EconomicCycle.Apply(ResolutionStage.XunUpdate, mutation, new([new(1, 10)], []), events);
        Assert.Equal(expected, mutation.Officers[2].Loyalty);
        Assert.Equal(123, mutation.Officers[2].LoyaltyTo(2));
        Assert.Equal(paid ? 0u : 9u, mutation.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Money);
        var payment = Assert.Single(events.OfType<ResourceChangedEvent>());
        Assert.Equal(paid ? -10 : 0, payment.Actual);
        Assert.Equal(paid ? 0 : 10, payment.Shortfall);
        Assert.Equal(random.State, Assert.Single(mutation.Simulation.RandomStreams).State);
        Assert.Equal(expected == loyalty ? 0 : 1, events.OfType<OfficerLoyaltyChangedEvent>().Count());
    }
}
