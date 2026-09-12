using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PersonnelDefectionTests
{
    private static readonly PersonnelRulebook Places = new([new(1, new(0, 0)), new(2, new(600, 0)), new(3, new(300, 0))]);
    private static WorldState World() => WorldState.Create(new(190, 1, 1), 190,
        [new(1, 10000, 10000, 1000) { LeaderId = 1, BaseTrust = 1000 }, new(2, 10000, 10000, 0) { LeaderId = 2 }],
        [new(1, 1, 100, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 1 },
         new(2, 2, 100, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 2 },
         new(3, 2, 1234, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 20, Population = 5555 }],
        [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(2, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(11, 1, 1, new(50, 50, 100, 50), false),
         new(20, 2, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Governor, IsCommander = true,
             ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, 100).Add(2, 50) },
         new(21, 2, 3, new(50, 50, 50, 50), false) { ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(2, 100) },
         new(22, 2, 3, new(50, 50, 50, 50), false) { ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(2, 0) }],
        units: [new(100, 2, new(300, 0), 100) { CorpsId = 20, ServicePlaceId = 3, IsEmbedded = true }],
        corps: [new(1, 1, 1, null), new(2, 2, 2, null), new(20, 2, 20, new(123, 456))],
        relations: [new(1, 2, 80)], controls: [new("player", 1, ControllerKind.Player)]);

    [Fact]
    public void CommanderDefectionTransfersObjectFollowersAndEmbeddedUnitWhileRefuserReturnsHome()
    {
        var engine = new SimulationEngine(World(), EconomyRulebook.Empty, Places);
        Assert.True(engine.Execute(new("player"), new RecruitOfficerCommand(Guid.NewGuid(), 1, 0, 11, 20)).Submission.Accepted);
        engine.EndControl(new("player"));
        var events = engine.AdvanceXun().Events;
        Assert.True(Assert.Single(events.OfType<RecruitmentResolvedEvent>()).Success);
        Assert.Single(events.OfType<PersonnelTerritoryTransferredEvent>());
        var city = engine.Snapshot.Settlements.Single(s => s.Id == 3);
        Assert.Equal(1, city.OwnerId);
        Assert.Equal(1, city.CorpsId);
        Assert.Equal(1234u, city.Soldiers);
        Assert.Equal(5555u, city.Population);
        Assert.Equal(1, Assert.Single(engine.Snapshot.Units).OwnerId);
        Assert.Equal(1, engine.Snapshot.Officers.Single(p => p.Id == 22).OwnerId);
        var refuser = engine.Snapshot.Officers.Single(p => p.Id == 21);
        Assert.Equal(2, refuser.OwnerId);
        Assert.Equal(2, refuser.SettlementId);
        Assert.Contains(engine.Snapshot.Simulation.Tasks, t => t.OfficerId == 21 && t.Kind == "personnel-return");
        Assert.DoesNotContain(engine.Snapshot.Corps, c => c.Id == 20);
        Assert.Contains(events, e => e is CorpsDissolvedEvent);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), EconomyRulebook.Empty, Places);
        engine.EndControl(new("player")); restored.EndControl(new("player"));
        Assert.Equal(CanonicalJson.Text(engine.AdvanceXun().Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
        Assert.Empty(restored.Snapshot.Simulation.Tasks);
    }

    [Fact]
    public void RulersServiceObjectCannotBeTakenThroughAnotherCommander()
    {
        var world = World();
        var change = world.BeginMutation();
        change.Replace(change.Officers[2] with { SettlementId = 3 });
        world.Commit(change);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, Places);
        string before = engine.Snapshot.StateHash;
        Assert.False(engine.Execute(new("player"), new RecruitOfficerCommand(Guid.NewGuid(), 1, 0, 11, 20)).Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }
}
