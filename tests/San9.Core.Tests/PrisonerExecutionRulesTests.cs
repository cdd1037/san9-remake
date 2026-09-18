using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PrisonerExecutionRulesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetentionAuthorityFollowsCurrentCorpsForFieldAndSettlementPrisoners(bool field)
    {
        var world = WorldState.Create(new(190, 1, 1), 190,
            [new(1, 1000, 1000, 0) { LeaderId = 1 }, new(2, 1000, 1000, 0) { LeaderId = 2 }],
            [new(1, 1, 100, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 10 },
             new(2, 2, 100, 100, 100, 0, 0, 0, 100, 0, 100),
             new(3, 1, 100, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 30 }],
            [new(1, 1, 1, new(80, 80, 80, 80), false) { Status = OfficerStatus.Ruler },
             new(2, 2, 2, new(80, 80, 80, 80), false) { Status = OfficerStatus.Ruler },
             new(3, 1, 3, new(80, 80, 80, 80), false) { Status = OfficerStatus.Governor },
             new(4, 1, null, new(50, 50, 50, 50), false) { UnitId = 0 },
             new(7, 2, field ? null : 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive, UnitId = field ? 0 : null }],
            units: [new(0, 1, new(7, 2), 100) { FormationId = 1, CorpsId = 30, ServicePlaceId = 3,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(4, BattleRank.Front) }],
            corps: [new(10, 1, 1, null), new(30, 1, 3, new(100, 100)) { ControlMode = CorpsControlMode.Automatic }],
            controls: [new("player", 1, ControllerKind.Player), new("ai:2", 2, ControllerKind.Automatic)]);
        var military = new MilitaryRulebook(new(3, 2, [0, 0, 0, 0, 0, 0], [new(0, 1, null)],
            [new(1, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 2), new(1, 1)), new(2, new(12, 2), new(1, 1)), new(3, new(2, 7), new(1, 1))]));
        var engine = new SimulationEngine(world, new SimulationRules { Military = military });
        var command = new ExecutePrisonerCommand(Guid.NewGuid(), 1, 0, 7);
        string before = engine.Snapshot.StateHash;
        Assert.Equal(CommandRejection.NotActiveController, engine.Execute(new("player"), command).Submission.Rejection);
        Assert.Equal(before, engine.Snapshot.StateHash);
        var result = engine.Execute(new(CorpsControl.ControllerId(30)), command);
        Assert.True(result.Submission.Accepted);
        Assert.Single(result.Events.OfType<OfficerDiedEvent>());
        var dead = engine.Snapshot.Officers.Single(p => p.Id == 7);
        Assert.Equal(OfficerStatus.Dead, dead.Status);
        Assert.Null(dead.UnitId);
        Assert.Null(dead.SettlementId);
        Assert.Equal(4, Assert.Single(engine.Snapshot.Units).CommanderId);
        Assert.NotNull(WorldState.Restore(engine.CaptureStableState()));
    }
}
