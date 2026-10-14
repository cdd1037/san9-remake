using San9.Tests;
using San9.Application.AI;
using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class MilitarySessionTests
{
    [Fact]
    public void UnitOrderPoliciesAndWaypointSurviveSaveLogAndRecording()
    {
        var definition = Fixture(); var session = GameSession.Start(definition, 42);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [1], 400, 7,
            new(MilitaryOrderKind.Move, new(32, 7), null)) { DepartureDelayDays = 9 }).Submission.Accepted);
        int unitId = Assert.Single(session.Snapshot.Units).Id;
        var command = new OrderUnitCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, unitId,
            new(MilitaryOrderKind.Return, null, 2)) { Policies = new(true, true, false, true), Waypoint = new(12, 2) };
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var unit = Assert.Single(restored.Snapshot.Units);
        Assert.Equal(command.Policies, unit.Policies); Assert.Equal(command.Waypoint, unit.Waypoint);
        Assert.Equal(0, unit.DepartureDaysRemaining);
        string path = Path.Combine(Path.GetTempPath(), $"unit-order-{Guid.NewGuid():N}.jsonl");
        try
        {
            CommandLogService.Write(path, session);
            var fromLog = GameSession.Start(definition, 42);
            var replayed = CommandLogService.Replay(path, fromLog);
            Assert.Null(replayed.FirstFailure); Assert.Equal(2, replayed.Accepted);
            StateAssert.Equal(session.Snapshot, fromLog.Snapshot);
        }
        finally { File.Delete(path); }
        EventAssert.Equal(session.AdvanceXun().Events, restored.AdvanceXun().Events);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Snapshot, replay.Session.Snapshot);
    }

    [Fact]
    public void DelayedWaypointDeploymentPersistsAndReplaysAsRealUnit()
    {
        var definition = Fixture(); var session = GameSession.Start(definition, 42);
        session.BeginRecording();
        var command = new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [1], 400, 7,
            new(MilitaryOrderKind.Move, new(32, 7), null))
            { DepartureDelayDays = 9, Waypoint = new(12, 2) };
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var unit = Assert.Single(restored.Snapshot.Units);
        Assert.Equal(9, unit.DepartureDaysRemaining); Assert.Equal(command.Waypoint, unit.Waypoint);
        StateAssert.Equal(session.Snapshot, restored.Snapshot);
        var result = session.AdvanceXun();
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        StateAssert.Equal(session.Snapshot, restored.Snapshot);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Snapshot, replay.Session.Snapshot);
    }

    [Fact]
    public void InteractiveDaysPublishMovementBeforeTheXunEndsAndMatchBatch()
    {
        var basis = Fixture();
        var definition = basis with { Military = basis.Military! with
        {
            Width = 100, TerrainIds = Enumerable.Repeat(0, 300).ToImmutableArray(),
            Terrains = [new(0, 12, null)],
            Formations = [basis.Military.Formations[0] with { MovementBudget = 95 }, basis.Military.Formations[1]]
        } };
        var batch = GameSession.Start(definition, 42);
        var interactive = GameSession.Start(definition, 42);
        var deploy = new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [1], 400, 7,
            new(MilitaryOrderKind.Move, new(402, 7), null));
        Assert.True(batch.Execute(new("player"), deploy).Submission.Accepted);
        Assert.True(interactive.Execute(new("player"), deploy).Submission.Accepted);
        var expected = batch.AdvanceXun();
        List<DomainEvent> events = [.. interactive.Apply(interactive.Request(new PrepareAdvanceOperation())).Events];
        for (int day = 2; day <= 11; day++)
        {
            var before = Assert.Single(interactive.Snapshot.Units);
            var pending = interactive.StartOperation(interactive.Request(new AdvanceDayOperation()));
            Assert.True(SpinWait.SpinUntil(() => pending.IsCompleted, TimeSpan.FromSeconds(10)));
            Assert.Equal(before, Assert.Single(interactive.Snapshot.Units));
            var movement = Assert.Single(pending.PreparedEvents.OfType<UnitMovedEvent>());
            Assert.NotEmpty(movement.Steps);
            Assert.Equal(movement.To, movement.Steps[^1]);
            Assert.Equal(movement.To, Assert.Single(pending.PreparedView.World.Units).Position);
            var result = pending.Complete();
            Assert.Equal(SessionDisposition.Accepted, result.Disposition);
            events.AddRange(result.Events);
            Assert.Equal(day, interactive.Snapshot.Clock.Day);
            Assert.NotEqual(before.Position, Assert.Single(interactive.Snapshot.Units).Position);
        }
        StateAssert.Equal(batch.Snapshot, interactive.Snapshot);
        EventAssert.Equal(expected.Events, events);
    }

    [Fact]
    public void AlienateAdviceMissionSaveAndRecordingUseThePublicCommandPath()
    {
        var basis = Fixture();
        var definition = basis with
        {
            Forces = [basis.Forces[0], new(2, "enemy", 2, 100)],
            Settlements = [basis.Settlements[0], basis.Settlements[1] with { OwnerId = 2 }],
            Officers = [basis.Officers[0] with { State = basis.Officers[0].State with
                { BaseAbilities = new(70, 100, 70, 70) } },
                new("target", new OfficerState(2, 2, 2, new(70, 50, 70, 70), false)
                    { Righteousness = 5 }.WithLoyalty(2, 80))],
            Information = basis.Information with { Corps = [..basis.Information.Corps, new(2, 2, null, new(10000, 10000)) { IsRoot = true }], Relations = [new(1, 2, 50)] }
        };
        var session = GameSession.Start(definition, 42);
        var before = session.Capture();
        Assert.Equal(AdviserOpinion.NoAdviser, session.GetAlienateAdvice(1, 1, 2).Opinion);
        StateAssert.Equal(before, session.Capture());
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new AlienateCommand(Guid.NewGuid(), 1, 0, 1, 2)).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);

        var result = session.AdvanceXun();
        Assert.Single(result.Events.OfType<AlienateResolvedEvent>());
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition,
            GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void GossipAdviceMissionSaveAndRecordingUseThePublicCommandPath()
    {
        var basis = Fixture();
        var definition = basis with
        {
            Forces = [basis.Forces[0] with { LeaderId = 1 },
                new ForceInitialState(2, "first-target", 2, 100) { LeaderId = 2, AdviserId = 3 },
                new ForceInitialState(3, "second-target", 3, 100) { LeaderId = 5 }],
            Settlements = [basis.Settlements[0], basis.Settlements[1] with { OwnerId = 2 },
                basis.Settlements[1] with { Id = 3, NameKey = "second-target", OwnerId = 3, X = 12 }],
            Officers = [basis.Officers[0] with { State = basis.Officers[0].State with
                { Status = OfficerStatus.Ruler, BaseAbilities = new(70, 100, 70, 70) } },
                new("first-ruler", new OfficerState(2, 2, 2, new(70, 40, 70, 70), false)
                    { Status = OfficerStatus.Ruler }),
                new("first-adviser", new OfficerState(3, 2, 2, new(70, 20, 70, 70), false)
                    { Status = OfficerStatus.Strategist }.WithLoyalty(2, 80)),
                new("first-officer", new OfficerState(4, 2, 2, new(70, 0, 70, 70), false).WithLoyalty(2, 20)),
                new("second-ruler", new OfficerState(5, 3, 3, new(70, 60, 70, 70), false)
                    { Status = OfficerStatus.Ruler })],
            Information = basis.Information with { Corps = [basis.Information.Corps[0] with { LeaderId = 1 }, new(2, 2, 2, new(10000, 10000)) { IsRoot = true }, new(3, 3, 5, new(10000, 10000)) { IsRoot = true }], Relations = [new(1, 2, 50), new(1, 3, 50), new(2, 3, 90)] },
            Military = basis.Military! with
            {
                Width = 20,
                TerrainIds = Enumerable.Repeat(0, 60).ToImmutableArray(),
                Places = [new(1, new(2, 7), new(1, 1)), new(2, new(37, 7), new(1, 1)),
                    new(3, new(62, 7), new(1, 1))]
            }
        };
        var session = GameSession.Start(definition, 42);
        var before = session.Capture();
        Assert.Equal(AdviserOpinion.NoAdviser, session.GetGossipAdvice(1, 1, 2, 3).Opinion);
        StateAssert.Equal(before, session.Capture());
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new GossipCommand(Guid.NewGuid(), 1, 0, 1, 2, 3))
            .Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);

        var result = session.AdvanceXun();
        Assert.Single(result.Events.OfType<GossipResolvedEvent>());
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition,
            GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void BurnAdviceAndBothJourneyLegsSurviveSaveAndRecording()
    {
        var basis = Fixture();
        var definition = basis with
        {
            Forces = [basis.Forces[0], new(2, "enemy", 2, 100)],
            Settlements = [basis.Settlements[0], basis.Settlements[1] with
                { OwnerId = 2, X = 66, Durability = 300, MaxDurability = 300 }],
            RegionNameKeys = new() { [1] = "origin", [2] = "destination" },
            RegionCityIds = new() { [1] = 1, [2] = 2 },
            CityConnections = [new(1, 2)],
            Information = basis.Information with { Corps = [..basis.Information.Corps, new(2, 2, null, new(10000, 10000)) { IsRoot = true }], Relations = [new(1, 2, 50)] },
            Military = basis.Military! with { Width = 200, TerrainIds = Enumerable.Repeat(0, 600).ToImmutableArray(),
                Places = [new(1, new(2, 7), new(1, 1)), new(2, new(332, 7), new(1, 1))] }
        };
        var session = GameSession.Start(definition, 42);
        var before = session.Capture();
        Assert.Equal(AdviserOpinion.NoAdviser, session.GetBurnAdvice(1, 1, 2).Opinion);
        StateAssert.Equal(before, session.Capture());
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new BurnCommand(Guid.NewGuid(), 1, 0, 1, 2)).Submission.Accepted);
        session.AdvanceXun();
        Assert.Equal("strategy-burn", Assert.Single(session.Snapshot.Simulation.Tasks).Kind);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var result = session.AdvanceXun();
        Assert.Single(result.Events.OfType<BurnResolvedEvent>());
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        Assert.Equal("personnel-return", Assert.Single(session.Snapshot.Simulation.Tasks).Kind);
        restored = SaveService.Decode(SaveService.Encode(session), definition);
        EventAssert.Equal(session.AdvanceXun().Events, restored.AdvanceXun().Events);
        Assert.Empty(session.Snapshot.Simulation.Tasks);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void SeizeAdviceAndBothJourneyLegsSurviveSaveAndRecording()
    {
        var basis = Fixture();
        var definition = basis with
        {
            Forces = [basis.Forces[0], new(2, "enemy", 2, 100)],
            Settlements = [basis.Settlements[0], basis.Settlements[1] with { OwnerId = 2, X = 66 }],
            Officers = [basis.Officers[0] with { State = basis.Officers[0].State with
                { BaseAbilities = new(100, 70, 70, 70) } }],
            RegionNameKeys = new() { [1] = "origin", [2] = "destination" },
            RegionCityIds = new() { [1] = 1, [2] = 2 },
            CityConnections = [new(1, 2)],
            Information = basis.Information with { Corps = [..basis.Information.Corps, new(2, 2, null, new(10000, 10000)) { IsRoot = true }], Relations = [new(1, 2, 50)] },
            Military = basis.Military! with { Width = 200, TerrainIds = Enumerable.Repeat(0, 600).ToImmutableArray(),
                Places = [new(1, new(2, 7), new(1, 1)), new(2, new(332, 7), new(1, 1))] }
        };
        var session = GameSession.Start(definition, 42);
        var before = session.Capture();
        Assert.Equal(AdviserOpinion.NoAdviser, session.GetSeizeAdvice(1, 1, 2).Opinion);
        StateAssert.Equal(before, session.Capture());
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new SeizeCommand(Guid.NewGuid(), 1, 0, 1, 2)).Submission.Accepted);
        session.AdvanceXun();
        Assert.Equal("strategy-seize", Assert.Single(session.Snapshot.Simulation.Tasks).Kind);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var result = session.AdvanceXun();
        Assert.Single(result.Events.OfType<SeizeResolvedEvent>());
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        Assert.Equal("personnel-return", Assert.Single(session.Snapshot.Simulation.Tasks).Kind);
        restored = SaveService.Decode(SaveService.Encode(session), definition);
        EventAssert.Equal(session.AdvanceXun().Events, restored.AdvanceXun().Events);
        Assert.Empty(session.Snapshot.Simulation.Tasks);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition,
            GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Theory]
    [InlineData(UnitStrategyKind.Inspire)]
    [InlineData(UnitStrategyKind.Reinforce)]
    public void UnitStrategyCommandAdviceAndBothJourneyLegsSurviveSaveAndRecording(UnitStrategyKind strategy)
    {
        var basis = Fixture();
        var definition = basis with
        {
            RegionNameKeys = new() { [1] = "destination-region" },
            RegionCityIds = new() { [1] = 1 },
            Settlements = [basis.Settlements[0] with { Soldiers = 10000 }, basis.Settlements[1]],
            Officers = [basis.Officers[0] with { State = basis.Officers[0].State with { BaseAbilities = new(70, 230, 70, 230) } },
                new("commander", new(2, 1, null, new(70, 70, 70, 80), false) { UnitId = 10 })],
            Units = [new(10, 1, new(332, 7), 9500) { FormationId = 7, Morale = 70, CommanderId = 2, }],
            Military = basis.Military! with { Width = 200, TerrainIds = Enumerable.Repeat(0, 600).ToImmutableArray(),
                RegionIds = Enumerable.Repeat<int?>(1, 600).ToImmutableArray() }
        };
        var session = GameSession.Start(definition, 42);
        var before = session.Capture();
        Assert.Equal(AdviserOpinion.NoAdviser, session.GetUnitStrategyAdvice(1, 1, strategy, 10).Opinion);
        StateAssert.Equal(before, session.Capture());
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new UnitStrategyCommand(Guid.NewGuid(), 1, 0, 1, strategy, 10)).Submission.Accepted);
        session.AdvanceXun();
        Assert.Equal("unit-strategy", Assert.Single(session.Snapshot.Simulation.Tasks).Kind);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var result = session.AdvanceXun();
        Assert.True(Assert.Single(result.Events.OfType<UnitStrategyResolvedEvent>()).Success);
        EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
        Assert.Equal("personnel-return", Assert.Single(session.Snapshot.Simulation.Tasks).Kind);
        if (strategy == UnitStrategyKind.Reinforce)
            Assert.Equal(3000u - Assert.Single(result.Events.OfType<UnitStrategyResolvedEvent>()).DeliveredSoldiers,
                Assert.Single(session.Snapshot.Simulation.Tasks).Reinforcements!.Soldiers);
        restored = SaveService.Decode(SaveService.Encode(session), definition);
        EventAssert.Equal(session.AdvanceXun().Events, restored.AdvanceXun().Events);
        Assert.Empty(session.Snapshot.Simulation.Tasks);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void TemporaryEngagementAndPendingPostActionSurviveSaveWithStrictFields()
    {
        var baseline = Fixture();
        var definition = baseline with
        {
            Forces = [baseline.Forces[0], new(2, "enemy", 2, 100)],
            Information = baseline.Information with { Corps = [..baseline.Information.Corps, new(2, 2, null, new(10000, 10000)) { IsRoot = true }] },
            Units = [new(700, 1, new(10, 7), 400) { FormationId = 7, Morale = 70,
                EngagementTargetId = 900, Order = new(MilitaryOrderKind.Move, new(20, 7), null), Policies = new(true, true, true, true) },
                new(900, 2, new(15, 7), 400) { FormationId = 7, Morale = 70, AwaitingPostAction = true }]
        };
        var session = GameSession.Start(definition, 42);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Equal(900, restored.Snapshot.Units.Single(u => u.Id == 700).EngagementTargetId);
        Assert.True(restored.Snapshot.Units.Single(u => u.Id == 900).AwaitingPostAction);
        Assert.Equal(session.Snapshot.Units[0].Policies, restored.Snapshot.Units[0].Policies);
    }

    [Fact]
    public void ArrivalQuerySharesUnitForecastAndRemainderSurvivesSaveAndReplay()
    {
        var basis = Fixture();
        var definition = basis with { Military = basis.Military! with
        {
            Width = 100, TerrainIds = Enumerable.Repeat(0, 300).ToImmutableArray(),
            Terrains = [new(0, 12, null)],
            Formations = [basis.Military.Formations[0] with { MovementBudget = 95 }, basis.Military.Formations[1]]
        } };
        var session = GameSession.Start(definition, 42);
        session.BeginRecording();
        Assert.True(session.Execute(new("player"), new DeployUnitCommand(Guid.NewGuid(), 1, 0,
            1, [1], 400, 7, new(MilitaryOrderKind.Move, new(402, 7), null))).Submission.Accepted);
        int id = Assert.Single(session.Snapshot.Units).Id;
        var view = session.View;
        var before = session.Capture();
        var texts = new San9.Content.Text.TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, new Dictionary<string, string>
        {
            ["officer"] = "甲", ["origin"] = "甲城", ["destination"] = "乙城"
        });
        using (var searches = new PathSearchDiagnostics())
        {
            var table = San9.Application.Presentation.OfficerCatalog.Read(view, texts, 2, null, null, 1);
            Assert.Equal(San9.Application.Presentation.CatalogCell.Unavailable,
                Assert.Single(table.Rows).Cells[Array.IndexOf(table.Columns, "到达预定")]);
            var detail = San9.Application.Presentation.OfficerDetails.Read(view, texts, 1);
            Assert.Equal("—", detail.Fields.SelectMany(f => f).Single(f => f.Label == "到达预定").Value);
            Assert.Equal(0, searches.Queries);
        }
        var estimate = view.UnitArrival(id);
        Assert.Equal(ArrivalEstimateStatus.Known, estimate.Status);
        using (var searches = new PathSearchDiagnostics())
        {
            Assert.Same(estimate, view.UnitArrival(id));
            Assert.Equal(0, searches.Queries);
        }
        StateAssert.Equal(before, session.Capture());
        session.AdvanceXun();
        Assert.Equal(2, Assert.Single(session.Snapshot.Units).MovementRemainder);
        Assert.Equal(estimate.Days - 10, session.View.UnitArrival(id).Days);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(2, Assert.Single(restored.Snapshot.Units).MovementRemainder);
        Assert.Equal(session.View.UnitArrival(id), restored.View.UnitArrival(id));
        EventAssert.Equal(session.AdvanceXun().Events, restored.AdvanceXun().Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
        Assert.Equal(session.View.UnitArrival(id), replay.Session.View.UnitArrival(id));
    }

    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion, "military-public", 190, 1, 1, 1,
        [new(1, "force", 1, 100)],
        [new(1, "origin", 1, 0, 1, 1000, 100, 100, 70, []) { Wounded = 123 }, new(2, "destination", 1, 7, 1, 100, 100, 100, 30, [])], [], [])
    {
        Officers = [new("officer", new(1, 1, 1, new(70, 70, 70, 70), false))],
        Military = new(8, 3, Enumerable.Repeat(0, 24).ToImmutableArray(), [new(0, 1, null)],
            [new(7, 1, 50, 10000, 5, new(1, 1), TravelMode.Land),
             new(70, 1, 50, 10000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 7), new(1, 1)), new(2, new(37, 7), new(1, 1))]) { TransportFormations = new(70, null) }
    , Information = new([], [], [], [new San9.Core.Domain.CorpsState(1, 1, null, new(10000, 10000)) { IsRoot = true }], []) };

    [Fact]
    public void PublicCommandSurvivesSaveAndRecordingWithCurrentLocationQueries()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 42);
        session.BeginRecording();
        var command = new TransportUnitCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence,
            1, [1], 400, 2, 200, 300) { Policies = new(true, false, true, false) };
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(command.Policies, Assert.Single(session.Snapshot.Units).Policies);
        Assert.Equal(1, Assert.Single(session.Snapshot.Units).CommanderId);
        var save = System.Text.Json.Nodes.JsonNode.Parse(SaveService.Encode(session))!;
        var savedUnit = save["session"]!["world"]!["units"]![0]!.AsObject();
        Assert.False(savedUnit.ContainsKey("memberOrder"));
        savedUnit["memberOrder"] = System.Text.Json.JsonSerializer.SerializeToNode(new[] { 1 });
        Assert.Equal(SaveFailure.InvalidFormat, Assert.Throws<SaveException>(() =>
            SaveService.Decode(System.Text.Encoding.UTF8.GetBytes(save.ToJsonString()), definition)).Failure);
        var policies = new UnitPolicies(false, true, false, true);
        Assert.True(session.Execute(new("player"), new SetUnitPoliciesCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, Assert.Single(session.Snapshot.Units).Id, policies)).Submission.Accepted);
        Assert.Equal(123u, session.View.Settlements.Single(s => s.Id == 1).Wounded);
        Assert.NotNull(session.View.Officers[0].UnitId);
        Assert.Null(session.View.Officers[0].SettlementId);
        Assert.False(session.View.Officers[0].IsAvailable);
        List<DomainEvent> transportEvents = [.. session.AdvanceXun().Events];
        Assert.NotEmpty(session.Snapshot.Units);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(policies, Assert.Single(restored.Snapshot.Units).Policies);
        Assert.Equal(1, Assert.Single(restored.Snapshot.Units).CommanderId);
        Assert.Equal(123u, restored.View.Settlements.Single(s => s.Id == 1).Wounded);
        for (int i = 0; i < 4; i++)
        {
            var first = session.AdvanceXun();
            transportEvents.AddRange(first.Events);
            var second = restored.AdvanceXun();
            EventAssert.Equal(first.Events, second.Events);
            StateAssert.Equal(session.Capture(), restored.Capture());
        }
        Assert.Empty(session.Snapshot.Units);
        Assert.Equal(2, Assert.Single(transportEvents.OfType<UnitAbsorbedEvent>()).SettlementId);
        Assert.Equal(1, session.View.Officers[0].SettlementId);
        Assert.Null(session.View.Officers[0].UnitId);
        Assert.DoesNotContain(session.Snapshot.Simulation.Tasks, task => task.OfficerId == 1);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void ChangedTerrainCostsAllowSaveAndUnknownFormationRejectsRestoration()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 42);
        Assert.True(session.Execute(new("player"), new DeployUnitCommand(
            Guid.NewGuid(), 1, 0, 1, [1], 400, 7, new(MilitaryOrderKind.Return, null, 2))).Submission.Accepted);
        byte[] saved = SaveService.Encode(session);
        var changed = definition with { Military = definition.Military! with { Terrains = [new(0, 2, null)] } };
        StateAssert.Equal(session.Capture(), SaveService.Decode(saved, changed).Capture());
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
            Information = baseline.Information with { Corps = [new(10, 1, 1, new(10000, 10000)) { ControlMode = CorpsControlMode.Automatic , IsRoot = true }] }
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfusionBlocksAutomaticAndDelegatedDeploymentThroughSharedValidation(bool delegated)
    {
        var baseline = Fixture();
        var definition = delegated ? ReinforcementFixture() : baseline with
        {
            PlayerForceId = 2,
            Forces = [baseline.Forces[0], new(2, "player-force", 2, 100)],
            Information = baseline.Information with { Corps = [..baseline.Information.Corps, new(2, 2, null, new(10000, 10000)) { IsRoot = true }] },
            Settlements = [baseline.Settlements[0] with { Soldiers = 10000 }, baseline.Settlements[1]]
        };
        var session = GameSession.Start(definition, 42);
        var snapshot = session.Capture();
        session = GameSession.Restore(definition, snapshot with { World = snapshot.World with
        {
            Settlements = snapshot.World.Settlements.Select(s => s.Id == 1 ? s with { ConfusionDaysRemaining = 12 } : s).ToArray()
        } });
        if (!delegated) session.EndControl(new("player"));
        var seat = new ControlSeat(delegated ? "corps:10" : "ai:1", 1, ControllerKind.Automatic);
        var hash = session.Capture();
        Assert.IsType<WaitCommand>(MilitaryPlanner.Choose(session, seat, Guid.NewGuid()));
        StateAssert.Equal(hash, session.Capture());
        Assert.Empty(session.AdvanceXun().Events.OfType<UnitDeployedEvent>());
        Assert.Empty(session.AdvanceXun().Events.OfType<UnitDeployedEvent>());
        Assert.Single(session.AdvanceXun().Events.OfType<UnitDeployedEvent>());
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
            EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
            StateAssert.Equal(session.Capture(), restored.Capture());
            events.AddRange(result.Events);
        }
        Assert.Empty(events.OfType<UnitDeployedEvent>());
        Assert.Single(events.OfType<UnitAbsorbedEvent>());
        Assert.Empty(session.Snapshot.Units);
        Assert.True(session.Snapshot.Settlements.Single(s => s.Id == 2).Soldiers >= 3000);
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
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
        var before = session.Capture();
        Assert.False(session.Validate(new("player"), Order()).Accepted);
        Assert.True(session.Validate(new("corps:10"), Order()).Accepted);
        Assert.False(session.Validate(new("player"), Policies()).Accepted);
        Assert.True(session.Validate(new("corps:10"), Policies()).Accepted);
        StateAssert.Equal(before, session.Capture());
        Assert.True(session.Execute(new("player"), new SetCorpsControlCommand(Guid.NewGuid(), 1,
            session.Snapshot.Simulation.NextSequence, 10, CorpsControlMode.Direct)).Submission.Accepted);
        Assert.False(session.Validate(new("corps:10"), Order()).Accepted);
        Assert.False(session.Validate(new("corps:10"), Policies()).Accepted);
        Assert.True(session.Execute(new("player"), Policies()).Submission.Accepted);
        Assert.True(session.Execute(new("player"), Order()).Submission.Accepted);
        Assert.Equal(1, Assert.Single(session.Snapshot.Units).Order!.SettlementId);
    }

    [Fact]
    public void AutomaticForceWithoutDelegatedCorpsAlsoDeploysAndDisconnectedPolicyIsReadOnly()
    {
        var baseline = Fixture();
        var definition = baseline with
        {
            PlayerForceId = 2,
            Forces = [baseline.Forces[0], new(2, "player-force", 2, 100)],
            Information = baseline.Information with { Corps = [..baseline.Information.Corps, new(2, 2, null, new(10000, 10000)) { IsRoot = true }] },
            Settlements = [baseline.Settlements[0] with { Soldiers = 10000 }, baseline.Settlements[1]]
        };
        var session = GameSession.Start(definition, 42);
        Assert.Single(session.AdvanceXun().Events.OfType<UnitDeployedEvent>());
        var blocked = definition with { Military = definition.Military! with { BlockedCells = [new(4, 0), new(4, 1), new(4, 2)] } };
        var waiting = GameSession.Start(blocked, 42);
        waiting.EndControl(new("player"));
        var before = waiting.Capture();
        Assert.IsType<WaitCommand>(MilitaryPlanner.Choose(waiting, new("ai:1", 1, ControllerKind.Automatic), Guid.NewGuid()));
        StateAssert.Equal(before, waiting.Capture());
        Assert.Empty(waiting.AdvanceXun().Events.OfType<UnitDeployedEvent>());
    }
}
