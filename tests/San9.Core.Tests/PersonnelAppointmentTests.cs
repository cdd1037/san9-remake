using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PersonnelAppointmentTests
{
    private static SimulationEngine Engine(int intelligence, int bonus = 0, int oldIntelligence = 80, ulong seed = 190, bool oldAdviser = true)
    {
        var world = WorldState.Create(new(190, 1, 1), seed,
            [new(1, 1000, 1000, 0) { LeaderId = 1, AdviserId = oldAdviser ? 11 : null }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new OfficerState(11, 1, 1, new(50, oldIntelligence, 50, 50), false) { Status = oldAdviser ? OfficerStatus.Strategist : OfficerStatus.Serving }.WithLoyalty(1, 1),
             new(12, 1, 1, new(50, intelligence, 50, 50), false) { ItemBonuses = new(0, bonus, 0, 0), OfficeBonuses = new(1, 0, 1, 0) }],
            controls: [new("player", 1, ControllerKind.Player)]);
        return new(world);
    }

    [Theory]
    [InlineData(69, false)]
    [InlineData(70, true)]
    public void AppointmentValidatesMinimumIntelligenceAndMaintainsAdviserIdentity(int intelligence, bool accepted)
    {
        var engine = Engine(intelligence);
        string before = engine.Snapshot.StateHash;
        var result = engine.Execute(new("player"), new AppointAdviserCommand(Guid.NewGuid(), 1, 0, 12));
        Assert.Equal(accepted, result.Submission.Accepted);
        if (!accepted) { Assert.Equal(before, engine.Snapshot.StateHash); return; }
        Assert.Equal(12, engine.Snapshot.Forces.Single().AdviserId);
        Assert.Equal(OfficerStatus.Serving, engine.Snapshot.Officers.Single(p => p.Id == 11).Status);
        var appointed = engine.Snapshot.Officers.Single(p => p.Id == 12);
        Assert.Equal(OfficerStatus.Strategist, appointed.Status);
        Assert.Equal(new OfficerAbilities(1, 0, 1, 0), appointed.OfficeBonuses);
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
