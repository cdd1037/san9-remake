using San9.Tests;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class AllianceVictorySessionTests
{
    [Fact]
    public void PublicSiegeAwardsVictoryOnceAndReplaysAcrossTheLastCityCapture()
    {
        var combat = new CombatProfile(10, 10, 100, 10, 50);
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "alliance-victory", 190, 1, 1, 1,
            [new(1, "first", 1, 100) { LeaderId = 11 },
                new(2, "target", 2, 100) { LeaderId = 21 },
                new(3, "ally", 3, 100) { LeaderId = 31 }],
            [new(1, "home", 1, 0, 0, 10000, 100, 100, 100, []),
                new(2, "target", 2, 2, 0, 1, 100, 100, 100, []),
                new(3, "ally", 3, 5, 0, 1000, 100, 100, 100, [])], [], [])
        {
            NaturalDeathsEnabled = false,
            Alliance = new(2, 3, [1, 3], 20),
            Officers = [new("attacker", new(11, 1, 1, new(100, 100, 100, 100), false) { Status = OfficerStatus.Ruler }),
                new("target", new(21, 2, 2, new(10, 10, 10, 10), false) { Status = OfficerStatus.Ruler }),
                new("ally", new(31, 3, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler })],
            Military = new(6, 1, [0, 0, 0, 0, 0, 0], [new(0, 1, null)],
                [new(1, 5, 0, 100000, 1, new(1, 1), TravelMode.Land)
                    { Combat = combat, Attack = new(5, false), CaptureRanks = [BattleRank.Middle] }],
                [new(1, new(2, 2), new(1, 1)) { Combat = combat },
                    new(2, new(12, 2), new(1, 1)) { Combat = combat },
                    new(3, new(27, 2), new(1, 1)) { Combat = combat }])
        , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 100000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 100000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 31, new(10000, 100000)) { IsRoot = true }], []) };
        var session = GameSession.Start(definition, 42);
        int trust = session.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust;
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new DeployUnitCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 1, [11], 5000, 1,
            new(MilitaryOrderKind.AttackSettlement, null, 2))).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        List<DomainEvent> events = [];
        for (int i = 0; i < 3 && !session.Snapshot.Forces.Single(f => f.Id == 2).IsEliminated; i++)
        {
            var advance = session.AdvanceXun();
            EventAssert.Equal(advance.Events, restored.AdvanceXun().Events);
            events.AddRange(advance.Events);
        }
        var victory = Assert.Single(events.OfType<AllianceVictoryEvent>());
        Assert.Equal((2, 1, AllianceVictoryCause.ForceDefeated, 15),
            (victory.TargetForceId, victory.VictorForceId, victory.Cause, victory.BaseTrustGained));
        Assert.Equal(trust + 15, session.Snapshot.Forces.Single(f => f.Id == 1).BaseTrust);
        Assert.Null(session.Snapshot.Simulation.Alliance);
        Assert.Single(events.OfType<AllianceEndedEvent>());
        Assert.True(session.Snapshot.Forces.Single(f => f.Id == 2).IsEliminated);
        StateAssert.Equal(session.Capture(), SaveService.Decode(SaveService.Encode(session), definition).Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }
}
