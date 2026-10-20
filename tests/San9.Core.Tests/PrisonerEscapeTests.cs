using San9.Tests;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PrisonerEscapeTests
{
    private static readonly PersonnelRulebook Places = new([new(1, new(0, 0)), new(2, new(120, 0))]);

    [Theory]
    [InlineData(0, 2)]
    [InlineData(59, 3)]
    [InlineData(60, 4)]
    [InlineData(79, 4)]
    [InlineData(80, 5)]
    [InlineData(99, 6)]
    [InlineData(100, 7)]
    [InlineData(int.MaxValue, 100)]
    public void EffectiveWarUsesSegmentedThreshold(int war, int expected) => Assert.Equal(expected, PrisonerEscapeCycle.Threshold(war));

    private static WorldMutation World(int count = 1, bool field = false, bool embedded = false, bool home = true)
    {
        var officers = Enumerable.Range(3, count).Select(id => new OfficerState(id, 2, field ? null : 1,
            new(100, 50, 50, 50), true) { Status = OfficerStatus.Captive, UnitId = field ? 10 : null, Righteousness = 15 }
            .WithLoyalty(1, 60).WithLoyalty(2, 80));
        return WorldState.Create(new(190, 1, 1), 190,
            [new(1, 0) { LeaderId = 1 }, new(2, 0) { LeaderId = 2 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, home ? 2 : 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 2, 2, new(50, 50, 50, 50), false) { Status = home ? OfficerStatus.Ruler : OfficerStatus.Captive }, .. officers],
            units: field ? [new(10, 1, new(0, 0), 100) { IsEmbedded = embedded }] : [], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 2, new(1000, 1000)) { IsRoot = true }]).BeginMutation();
    }

    private static void Rolls(WorldMutation world, int roll)
    {
        ulong seed = TestSeeds.StateForRoll(roll);
        world.SetSimulation(world.Simulation with { RandomStreams = [new("personnel-prisoner-escape", seed)] });
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public void BoundaryRestoresAllegianceWithoutReleaseReward(int roll, bool escapes)
    {
        var world = World();
        Rolls(world, roll);
        List<DomainEvent> events = [];
        PrisonerEscapeCycle.Apply(world, Places, EconomyRulebook.Empty, events);
        Assert.Equal(escapes ? OfficerStatus.Serving : OfficerStatus.Captive, world.Officers[3].Status);
        Assert.Equal(60, world.Officers[3].LoyaltyTo(1));
        Assert.Equal(80, world.Officers[3].LoyaltyTo(2));
        Assert.Equal(escapes ? 1 : 0, events.OfType<PrisonerEscapedEvent>().Count());
        Assert.Equal(escapes ? 1 : 0, world.Simulation.Tasks.Length);
        WorldInvariants.Validate(world);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyEmbeddedFieldDetentionParticipates(bool embedded)
    {
        var world = World(field: true, embedded: embedded);
        Rolls(world, 0);
        var before = world.Simulation.RandomStreams;
        List<DomainEvent> events = [];
        PrisonerEscapeCycle.Apply(world, Places, EconomyRulebook.Empty, events);
        Assert.Equal(embedded ? OfficerStatus.Serving : OfficerStatus.Captive, world.Officers[3].Status);
        if (embedded)
        {
            Assert.Null(world.Officers[3].UnitId);
            var escaped = Assert.Single(events.OfType<PrisonerEscapedEvent>());
            Assert.Equal(new PersonOccurrence(2, 1, new(0, 0)), escaped.Occurrence);
            Assert.Equal(1, escaped.CaptorForceId);
        }
        else Assert.Equal(before, world.Simulation.RandomStreams);
    }

    [Fact]
    public void SuccessfulRollWithoutDestinationLeavesNoTasksOrSequenceChanges()
    {
        var world = World(home: false);
        Rolls(world, 0);
        var person = world.Officers[3];
        List<DomainEvent> events = [];
        PrisonerEscapeCycle.Apply(world, Places, EconomyRulebook.Empty, events);
        Assert.Equal(person, world.Officers[3]);
        Assert.Empty(events);
        Assert.Empty(world.Simulation.Tasks);
        Assert.Equal(0, world.Simulation.NextSequence);
    }

    [Fact]
    public void GlobalBudgetCountsSuccessesAndUsesEffectiveWarOrdering()
    {
        var world = World(5);
        // These unusually high public fixture abilities guarantee success without a probabilistic test.
        foreach (int id in Enumerable.Range(3, 5))
            world.Replace(world.Officers[id] with { BaseAbilities = new(2000 + id, 50, 50, 50) });
        List<DomainEvent> events = [];
        PrisonerEscapeCycle.Apply(world, Places, EconomyRulebook.Empty, events);
        Assert.Equal(new[] { 7, 6, 5 }, events.OfType<PrisonerEscapedEvent>().Select(e => e.OfficerId));
        Assert.Equal(3, world.Simulation.NextSequence);
        Assert.Equal(OfficerStatus.Captive, world.Officers[4].Status);
    }
}
