using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PersonnelAppointmentTests
{
    private static SimulationEngine Engine(int intelligence, int bonus = 0, int oldIntelligence = 80, ulong seed = 190, bool oldAdviser = true)
    {
        var world = WorldState.Create(new(190, 1, 1), seed,
            [new(1, 0) { LeaderId = 1, AdviserId = oldAdviser ? 11 : null }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new OfficerState(11, 1, 1, new(50, oldIntelligence, 50, 50), false) { Status = oldAdviser ? OfficerStatus.Strategist : OfficerStatus.Serving }.WithLoyalty(1, 1),
             new(12, 1, 1, new(50, intelligence, 50, 50), false) { OfficeId = 1 }],
            treasures: [new(10, 12, TreasureDiscovery.Discovered, null)], controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(1000, 1000)) { IsRoot = true }]);
        return new(world, new([new(1, 0)], []), PersonnelRulebook.Empty, new([new(10, new(0, bonus, 0, 0), 0)]),
            new([new(1, 10000, 0) { AbilityBonuses = new(1, 0, 1, 0) }]));
    }

    [Theory]
    [InlineData(69, false)]
    [InlineData(70, true)]
    public void AppointmentValidatesMinimumIntelligenceAndMaintainsAdviserIdentity(int intelligence, bool accepted)
    {
        var engine = Engine(intelligence);
        var preview = new AppointAdviserCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, 12);
        string before = engine.Snapshot.StateHash;
        Assert.Equal(accepted, engine.GetPreparationEligibility(new("player"), preview) == CommandRejection.None);
        Assert.Equal(before, engine.Snapshot.StateHash);
        var result = engine.Execute(new("player"), new AppointAdviserCommand(Guid.NewGuid(), 1, 0, 12));
        Assert.Equal(accepted, result.Submission.Accepted);
        if (!accepted) { Assert.Equal(before, engine.Snapshot.StateHash); return; }
        Assert.Equal(12, engine.Snapshot.Forces.Single().AdviserId);
        Assert.Equal(OfficerStatus.Serving, engine.Snapshot.Officers.Single(p => p.Id == 11).Status);
        var appointed = engine.Snapshot.Officers.Single(p => p.Id == 12);
        Assert.Equal(OfficerStatus.Strategist, appointed.Status);
        Assert.Equal(1, appointed.OfficeId);
        Assert.Equal(51, engine.GetAbilities(engine.Snapshot)[appointed.Id].War);
        Assert.False(appointed.Acted);
        Assert.Single(result.Events.OfType<AdviserAppointedEvent>());
    }

    [Theory]
    [InlineData(70, 25, true, false)]
    [InlineData(60, 20, true, true)]
    [InlineData(90, 0, true, false)]
    [InlineData(70, 0, false, false)]
    public void CurrentIntelligenceComparisonControlsReplacementRandom(int intelligence, int bonus, bool oldAdviser, bool consumesRandom)
    {
        var engine = Engine(intelligence, bonus, oldAdviser: oldAdviser);
        Assert.True(engine.Execute(new("player"), new AppointAdviserCommand(Guid.NewGuid(), 1, 0, 12)).Submission.Accepted);
        Assert.Equal(consumesRandom, engine.Snapshot.Simulation.RandomStreams.Any(s => s.Name == "personnel-adviser-replacement"));
    }

    [Fact]
    public void ServingCorpsLeaderCanBecomeAdviserWithoutLosingCorpsOrActionState()
    {
        var state = Engine(80).Snapshot;
        var engine = new SimulationEngine(WorldState.Restore(state with
        {
            Corps = [new(3, 1, 12, new(1000, 1000)) { IsRoot = true }],
            Settlements = state.Settlements.Select(s => s with { CorpsId = 3 }).ToArray(),
            Officers = state.Officers.Select(p => p.Id == 12 ? p with { Acted = true } : p).ToArray()
        }), new([new(1, 0)], []), PersonnelRulebook.Empty, new([new(10, new(0, 0, 0, 0), 0)]),
            new([new(1, 10000, 0) { AbilityBonuses = new(1, 0, 1, 0) }]));
        Assert.True(engine.Execute(new("player"), new AppointAdviserCommand(Guid.NewGuid(), 1, 0, 12)).Submission.Accepted);
        Assert.Equal(12, engine.Snapshot.Forces.Single().AdviserId);
        Assert.Equal(12, engine.Snapshot.Corps.Single().LeaderId);
        Assert.True(engine.Snapshot.Officers.Single(p => p.Id == 12).Acted);
        Assert.Equal(engine.Snapshot.StateHash, WorldState.Restore(engine.CaptureStableState()).CreateSnapshot().StateHash);
    }

    [Fact]
    public void EqualIntelligenceConsumesExactlyOneRollAndSaturatesOldLoyalty()
    {
        HashSet<int> rolls = [];
        for (ulong seed = 0; seed < 30 && rolls.Count < 3; seed++)
        {
            var engine = Engine(80, seed: seed);
            var random = new StableRandom(StableRandom.DeriveSeed(seed, "personnel-adviser-replacement"));
            int loss = (int)(random.NextUInt64() % 3);
            rolls.Add(loss);
            Assert.True(engine.Execute(new("player"), new AppointAdviserCommand(Guid.NewGuid(), 1, 0, 12)).Submission.Accepted);
            Assert.Equal(Math.Max(0, 1 - loss), engine.Snapshot.Officers.Single(p => p.Id == 11).Loyalty);
            Assert.Equal(random.State, engine.Snapshot.Simulation.RandomStreams.Single(s => s.Name == "personnel-adviser-replacement").State);
        }
        Assert.Equal(3, rolls.Count);
    }
}
