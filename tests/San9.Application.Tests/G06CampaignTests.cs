using San9.Application.AI;
using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class G06CampaignTests
{
    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion, "g06-campaign", 190, 1, 1, 1,
        [new(1, "attacker", 1, 100000, 100000, 100) { LeaderId = 11 }, new(2, "defender", 2, 1000, 1000, 100) { LeaderId = 22 }],
        [new(1, "origin", 1, 0, 1, 5000, 100, 100, 90, []) { DraftPopulation = 10000, PublicOrder = 900, MaxCommerce = 100, MaxCultivation = 100 },
         new(2, "target", 2, 7, 1, 100, 1, 100, 30, [])], [], [])
    {
        Officers = [new("ruler", new(11, 1, 1, new(90, 90, 90, 90), false) { Status = OfficerStatus.Ruler }),
            new("general", new(12, 1, 1, new(90, 90, 90, 90), false)),
            new("enemy", new(22, 2, 2, new(40, 40, 40, 40), false) { Status = OfficerStatus.Ruler })],
        Military = new(8, 3, Enumerable.Repeat(0, 24).ToImmutableArray(), [new(0, 1, null)],
            [new(7, 2, 10, 100000, 5, new(1, 1), TravelMode.Land) { Combat = new(10, 10, 10, 10, 50), Attack = new(1, true), CaptureRanks = [BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle] }],
            [new(1, new(2, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50), Attack = new(1, false) },
             new(2, new(37, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50), Attack = new(1, false) }])
    };

    [Fact]
    public void EconomyRecruitmentPlannerDeploymentConquestSaveAndRecordingUsePublicSession()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 42);
        session.BeginRecording();
        var events = new List<DomainEvent>();
        foreach (var activity in new[] { DomesticActivity.Commerce, DomesticActivity.Recruit })
        {
            var result = session.Execute(new("player"), new DevelopCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
                new(1, 1, [12], activity)));
            Assert.True(result.Submission.Accepted, $"{activity}: {result.Submission.Rejection}");
            events.AddRange(result.Events);
            events.AddRange(session.AdvanceXun().Events);
        }
        string before = session.Capture().StateHash;
        var attack = MilitaryPlanner.Choose(session, new("player", 1, ControllerKind.Player), Guid.NewGuid());
        Assert.IsType<DeployUnitCommand>(attack);
        Assert.Equal(before, session.Capture().StateHash);
        var deployed = session.Execute(new("player"), attack);
        Assert.True(deployed.Submission.Accepted);
        events.AddRange(deployed.Events);
        events.AddRange(session.AdvanceXun().Events);
        Assert.NotEmpty(session.Snapshot.Units);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        for (int i = 0; i < 10 && session.Snapshot.Simulation.Boundary != SimulationBoundary.Terminal; i++)
        {
            var result = session.AdvanceXun();
            Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
            Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
            events.AddRange(result.Events);
        }
        Assert.Equal(SimulationBoundary.Terminal, session.Snapshot.Simulation.Boundary);
        Assert.Single(events.OfType<SettlementCapturedEvent>());
        Assert.Single(events.OfType<ForceEliminatedEvent>());
        Assert.Contains(events.OfType<DevelopmentCompletedEvent>(), e => e.Command.Activity == DomesticActivity.Recruit);
        Assert.True(session.View.Forces.Single(f => f.Id == 2).IsEliminated);
        Assert.Equal(session.Capture().StateHash, SaveService.Decode(SaveService.Encode(session), definition).Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void AutomaticForceAttacksThroughPublicCommandsAndPlayerDefeatStopsFurtherWork()
    {
        var initial = Fixture();
        var definition = initial with { PlayerForceId = 2,
            Settlements = [initial.Settlements[0] with { Soldiers = 10000 }, initial.Settlements[1]] };
        var session = GameSession.Start(definition, 42);
        session.BeginRecording();
        var events = new List<DomainEvent>();
        for (int i = 0; i < 10 && session.Snapshot.Simulation.Boundary != SimulationBoundary.Terminal; i++)
            events.AddRange(session.AdvanceXun().Events);
        Assert.Contains(events, e => e is UnitDeployedEvent);
        Assert.Contains(events, e => e is SettlementCapturedEvent);
        Assert.Equal("player-defeated", session.Snapshot.Simulation.TerminalReason);
        Assert.DoesNotContain(session.Snapshot.Simulation.Controls, c => c.ForceId == 2);
        string hash = session.Capture().StateHash;
        Assert.Empty(session.AdvanceXun().Events);
        Assert.Equal(hash, session.Capture().StateHash);
        Assert.Equal(hash, SaveService.Decode(SaveService.Encode(session), definition).Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(hash, replay.Session.Capture().StateHash);
    }
}
