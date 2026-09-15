using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class MilitarySessionTests
{
    [Fact]
    public void AlienateAdviceMissionSaveAndRecordingUseThePublicCommandPath()
    {
        var basis = Fixture();
        var definition = basis with
        {
            Forces = [basis.Forces[0], new(2, "enemy", 2, 10000, 10000, 100)],
            Settlements = [basis.Settlements[0], basis.Settlements[1] with { OwnerId = 2 }],
            Officers = [basis.Officers[0] with { State = basis.Officers[0].State with
                { BaseAbilities = new(70, 100, 70, 70) } },
                new("target", new OfficerState(2, 2, 2, new(70, 50, 70, 70), false)
                    { Righteousness = 5 }.WithLoyalty(2, 80))],
            Information = basis.Information with { Relations = [new(1, 2, 50)] }
        };
        var session = GameSession.Start(definition, 42);
        string before = session.Capture().StateHash;
        Assert.Equal(AdviserOpinion.NoAdviser, session.GetAlienateAdvice(1, 1, 2).Opinion);
        Assert.Equal(before, session.Capture().StateHash);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new AlienateCommand(Guid.NewGuid(), 1, 0, 1, 2)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);

        var result = session.AdvanceXun();
        Assert.Single(result.Events.OfType<AlienateResolvedEvent>());
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition,
            GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void BurnAdviceAndBothJourneyLegsSurviveSaveAndRecording()
    {
        var basis = Fixture();
        var definition = basis with
        {
            Forces = [basis.Forces[0], new(2, "enemy", 2, 10000, 10000, 100)],
            Settlements = [basis.Settlements[0], basis.Settlements[1] with
                { OwnerId = 2, X = 66, Durability = 300, MaxDurability = 300 }],
            RegionNameKeys = new() { [1] = "origin", [2] = "destination" },
            RegionCityIds = new() { [1] = 1, [2] = 2 },
            CityConnections = [new(1, 2)],
            Information = basis.Information with { Relations = [new(1, 2, 50)] },
            Military = basis.Military! with { Width = 200, TerrainIds = Enumerable.Repeat(0, 600).ToImmutableArray(),
                Places = [new(1, new(2, 7), new(1, 1)), new(2, new(332, 7), new(1, 1))] }
        };
        var session = GameSession.Start(definition, 42);
        string before = session.Capture().StateHash;
        Assert.Equal(AdviserOpinion.NoAdviser, session.GetBurnAdvice(1, 1, 2).Opinion);
        Assert.Equal(before, session.Capture().StateHash);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new BurnCommand(Guid.NewGuid(), 1, 0, 1, 2)).Submission.Accepted);
        session.AdvanceXun();
        Assert.Equal("strategy-burn", Assert.Single(session.Snapshot.Simulation.Tasks).Kind);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var result = session.AdvanceXun();
        Assert.Single(result.Events.OfType<BurnResolvedEvent>());
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal("personnel-return", Assert.Single(session.Snapshot.Simulation.Tasks).Kind);
        restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(CanonicalJson.Text(session.AdvanceXun().Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Empty(session.Snapshot.Simulation.Tasks);
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void SeizeAdviceAndBothJourneyLegsSurviveSaveAndRecording()
    {
        var basis = Fixture();
        var definition = basis with
        {
            Forces = [basis.Forces[0], new(2, "enemy", 2, 10000, 10000, 100)],
            Settlements = [basis.Settlements[0], basis.Settlements[1] with { OwnerId = 2, X = 66 }],
            Officers = [basis.Officers[0] with { State = basis.Officers[0].State with
                { BaseAbilities = new(100, 70, 70, 70) } }],
            RegionNameKeys = new() { [1] = "origin", [2] = "destination" },
            RegionCityIds = new() { [1] = 1, [2] = 2 },
            CityConnections = [new(1, 2)],
            Information = basis.Information with { Relations = [new(1, 2, 50)] },
            Military = basis.Military! with { Width = 200, TerrainIds = Enumerable.Repeat(0, 600).ToImmutableArray(),
                Places = [new(1, new(2, 7), new(1, 1)), new(2, new(332, 7), new(1, 1))] }
        };
        var session = GameSession.Start(definition, 42);
        string before = session.Capture().StateHash;
        Assert.Equal(AdviserOpinion.NoAdviser, session.GetSeizeAdvice(1, 1, 2).Opinion);
        Assert.Equal(before, session.Capture().StateHash);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SeizeCommand(Guid.NewGuid(), 1, 0, 1, 2)).Submission.Accepted);
        session.AdvanceXun();
        Assert.Equal("strategy-seize", Assert.Single(session.Snapshot.Simulation.Tasks).Kind);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var result = session.AdvanceXun();
        Assert.Single(result.Events.OfType<SeizeResolvedEvent>());
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal("personnel-return", Assert.Single(session.Snapshot.Simulation.Tasks).Kind);
        restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(CanonicalJson.Text(session.AdvanceXun().Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Empty(session.Snapshot.Simulation.Tasks);
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition,
            GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void UnitStrategyCommandAdviceAndBothJourneyLegsSurviveSaveAndRecording()
    {
        var basis = Fixture();
        var definition = basis with
        {
            RegionNameKeys = new() { [2] = "destination-region" },
            Officers = [basis.Officers[0] with { State = basis.Officers[0].State with { BaseAbilities = new(70, 230, 70, 70) } },
                new("commander", new(2, 1, null, new(70, 70, 70, 80), false) { UnitId = 10 })],
            Units = [new(10, 1, new(332, 7), 400) { FormationId = 7, Morale = 70, CommanderId = 2, MemberOrder = [2] }],
            Military = basis.Military! with { Width = 200, TerrainIds = Enumerable.Repeat(0, 600).ToImmutableArray(),
                RegionIds = Enumerable.Repeat<int?>(2, 600).ToImmutableArray() }
        };
        var session = GameSession.Start(definition, 42);
        string before = session.Capture().StateHash;
        Assert.Equal(AdviserOpinion.NoAdviser, session.GetUnitStrategyAdvice(1, 1, UnitStrategyKind.Inspire, 10).Opinion);
        Assert.Equal(before, session.Capture().StateHash);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new UnitStrategyCommand(Guid.NewGuid(), 1, 0, 1, UnitStrategyKind.Inspire, 10)).Submission.Accepted);
        session.AdvanceXun();
        Assert.Equal("unit-strategy", Assert.Single(session.Snapshot.Simulation.Tasks).Kind);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var result = session.AdvanceXun();
        Assert.True(Assert.Single(result.Events.OfType<UnitStrategyResolvedEvent>()).Success);
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal("personnel-return", Assert.Single(session.Snapshot.Simulation.Tasks).Kind);
        restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(CanonicalJson.Text(session.AdvanceXun().Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Empty(session.Snapshot.Simulation.Tasks);
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void TemporaryEngagementAndPendingPostActionSurviveSaveWithStrictFields()
    {
        var baseline = Fixture();
        var definition = baseline with
        {
            Forces = [baseline.Forces[0], new(2, "enemy", 2, 10000, 10000, 100)],
            Units = [new(700, 1, new(10, 7), 400) { FormationId = 7, Morale = 70,
                EngagementTargetId = 900, Order = new(MilitaryOrderKind.Move, new(20, 7), null), Policies = new(true, true, true, true) },
                new(900, 2, new(15, 7), 400) { FormationId = 7, Morale = 70, AwaitingPostAction = true }]
        };
        var session = GameSession.Start(definition, 42);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        Assert.Equal(900, restored.Snapshot.Units.Single(u => u.Id == 700).EngagementTargetId);
        Assert.True(restored.Snapshot.Units.Single(u => u.Id == 900).AwaitingPostAction);
        Assert.Equal(session.Snapshot.Units[0].Policies, restored.Snapshot.Units[0].Policies);
    }

    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion, "military-public", 190, 1, 1, 1,
        [new(1, "force", 1, 10000, 10000, 100)],
        [new(1, "origin", 1, 0, 1, 1000, 100, 100, 70, []) { Wounded = 123 }, new(2, "destination", 1, 7, 1, 100, 100, 100, 30, [])], [], [])
    {
        Officers = [new("officer", new(1, 1, 1, new(70, 70, 70, 70), false))],
        Military = new(8, 3, Enumerable.Repeat(0, 24).ToImmutableArray(), [new(0, 1, null)],
            [new(7, 1, 50, 10000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 7), new(1, 1)), new(2, new(37, 7), new(1, 1))])
    };

    [Fact]
    public void PublicCommandSurvivesSaveAndRecordingWithCurrentLocationQueries()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 42);
        session.BeginRecording();
        var command = new DeployUnitCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
            1, [1], 400, 7, new(MilitaryOrderKind.Transport, null, 2), 200, 300) { Policies = new(true, false, true, false) };
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(command.Policies, Assert.Single(session.Snapshot.Units).Policies);
        Assert.Equal(1, Assert.Single(session.Snapshot.Units).CommanderId);
        Assert.Equal([1], Assert.Single(session.Snapshot.Units).MemberOrder.ToArray());
        var policies = new UnitPolicies(false, true, false, true);
        Assert.True(session.Execute(new("player"), new SetUnitPoliciesCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, Assert.Single(session.Snapshot.Units).Id, policies)).Submission.Accepted);
        Assert.Equal(123u, session.View.Settlements.Single(s => s.Id == 1).Wounded);
        Assert.NotNull(session.View.Officers[0].UnitId);
        Assert.Null(session.View.Officers[0].SettlementId);
        Assert.False(session.View.Officers[0].IsAvailable);
        session.AdvanceXun();
        Assert.NotEmpty(session.Snapshot.Units);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(policies, Assert.Single(restored.Snapshot.Units).Policies);
        Assert.Equal([1], Assert.Single(restored.Snapshot.Units).MemberOrder.ToArray());
        Assert.Equal(123u, restored.View.Settlements.Single(s => s.Id == 1).Wounded);
        for (int i = 0; i < 4; i++)
        {
            var first = session.AdvanceXun();
            var second = restored.AdvanceXun();
            Assert.Equal(CanonicalJson.Text(first.Events), CanonicalJson.Text(second.Events));
            Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
        }
        Assert.Empty(session.Snapshot.Units);
        Assert.Equal(2, session.View.Officers[0].SettlementId);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void DifferentTopologyRejectsSaveAndUnknownFormationRejectsRestoration()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 42);
        Assert.True(session.Execute(new("player"), new DeployUnitCommand(
            Guid.NewGuid(), 1, 0, 1, [1], 400, 7, new(MilitaryOrderKind.Return, null, 2))).Submission.Accepted);
        byte[] saved = SaveService.Encode(session);
        var changed = definition with { Military = definition.Military! with { Terrains = [new(0, 2, null)] } };
        Assert.Equal(SaveFailure.ContentMismatch, Assert.Throws<SaveException>(() => SaveService.Decode(saved, changed)).Failure);
        var capture = session.Capture();
        var invalid = capture with { World = capture.World with { Units = [capture.World.Units[0] with { FormationId = 900 }] } };
        Assert.Throws<ArgumentException>(() => GameSession.Restore(definition, invalid));
    }

    private static ScenarioInitialState ReinforcementFixture()
    {
        var baseline = Fixture();
        return baseline with
        {
            Forces = [baseline.Forces[0] with { LeaderId = 1 }],
            Settlements = [baseline.Settlements[0] with { Soldiers = 10000, CorpsId = 10 }, baseline.Settlements[1] with { CorpsId = 10 }],
            Officers = [baseline.Officers[0] with { State = baseline.Officers[0].State with { Status = OfficerStatus.Ruler } }],
            Information = baseline.Information with { Corps = [new(10, 1, 1, null) { ControlMode = CorpsControlMode.Automatic }] }
        };
    }

    [Fact]
    public void AutomaticCorpsReinforcesThroughPublicCommandsAndReplaysInFlight()
    {
        var definition = ReinforcementFixture();
        var session = GameSession.Start(definition, 42);
        session.BeginRecording();
        var first = session.AdvanceXun();
        Assert.Single(first.Events.OfType<UnitDeployedEvent>());
        Assert.Equal(2900u, Assert.Single(session.Snapshot.Units).Soldiers);
        Assert.IsType<WaitCommand>(MilitaryPlanner.Choose(session, new("corps:10", 1, ControllerKind.Automatic), Guid.NewGuid()));
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var events = new List<DomainEvent>();
        for (int i = 0; i < 4; i++)
        {
            var result = session.AdvanceXun();
            Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
            Assert.Equal(session.Capture().StateHash, restored.Capture().StateHash);
            events.AddRange(result.Events);
        }
        Assert.Empty(events.OfType<UnitDeployedEvent>());
        Assert.Single(events.OfType<UnitAbsorbedEvent>());
        Assert.Empty(session.Snapshot.Units);
        Assert.True(session.Snapshot.Settlements.Single(s => s.Id == 2).Soldiers >= 3000);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        Assert.Equal(session.Capture().StateHash, replay.Session.Capture().StateHash);
    }

    [Fact]
    public void ControlChangeImmediatelyChangesFieldOrderAuthority()
    {
        var session = GameSession.Start(ReinforcementFixture(), 42);
        session.AdvanceXun();
        int unitId = Assert.Single(session.Snapshot.Units).Id;
        OrderUnitCommand Order() => new(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
            unitId, new(MilitaryOrderKind.Return, null, 1));
        SetUnitPoliciesCommand Policies() => new(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, unitId, new(true, true, true, true));
        string before = session.Capture().StateHash;
        Assert.False(session.Validate(new("player"), Order()).Accepted);
        Assert.True(session.Validate(new("corps:10"), Order()).Accepted);
        Assert.False(session.Validate(new("player"), Policies()).Accepted);
        Assert.True(session.Validate(new("corps:10"), Policies()).Accepted);
        Assert.Equal(before, session.Capture().StateHash);
        Assert.True(session.Execute(new("player"), new SetCorpsControlCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 10, CorpsControlMode.Direct)).Submission.Accepted);
        Assert.False(session.Validate(new("corps:10"), Order()).Accepted);
        Assert.False(session.Validate(new("corps:10"), Policies()).Accepted);
        Assert.True(session.Execute(new("player"), Policies()).Submission.Accepted);
        Assert.True(session.Execute(new("player"), Order()).Submission.Accepted);
        Assert.Equal(1, Assert.Single(session.Snapshot.Units).Order!.SettlementId);
    }

    [Fact]
    public void AutomaticForceWithoutCorpsAlsoDeploysAndDisconnectedPolicyIsReadOnly()
    {
        var baseline = Fixture();
        var definition = baseline with
        {
            PlayerForceId = 2,
            Forces = [baseline.Forces[0], new(2, "player-force", 2, 10000, 10000, 100)],
            Settlements = [baseline.Settlements[0] with { Soldiers = 10000 }, baseline.Settlements[1]]
        };
        var session = GameSession.Start(definition, 42);
        Assert.Single(session.AdvanceXun().Events.OfType<UnitDeployedEvent>());
        var blocked = definition with { Military = definition.Military! with { BlockedCells = [new(4, 0), new(4, 1), new(4, 2)] } };
        var waiting = GameSession.Start(blocked, 42);
        waiting.EndControl(new("player"));
        string before = waiting.Capture().StateHash;
        Assert.IsType<WaitCommand>(MilitaryPlanner.Choose(waiting, new("ai:1", 1, ControllerKind.Automatic), Guid.NewGuid()));
        Assert.Equal(before, waiting.Capture().StateHash);
        Assert.Empty(waiting.AdvanceXun().Events.OfType<UnitDeployedEvent>());
    }
}
