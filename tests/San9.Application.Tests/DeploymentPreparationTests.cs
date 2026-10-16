using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class DeploymentPreparationTests
{
    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion, "campaign", 190, 1, 1, 1,
        [new(1, "opponent", 1, 100) { LeaderId = 1 }, new(2, "automatic", 2, 100) { LeaderId = 2 }],
        [new(1, "rear", 1, 18, 1, 1000, 1000, 1000, 100, []),
         new(2, "source", 2, 0, 1, 14000, 1000, 1000, 100, []) { MerchantPresenceChance = 100 },
         new(3, "objective", 1, 5, 1, 2000, 30, 100, 100, [])], [], [])
    {
        Officers = [new("enemy-ruler", new(1, 1, 1, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler }),
            new("ruler", new(2, 2, 2, new(70, 70, 95, 70), false) { Status = OfficerStatus.Ruler }),
            new("general", new OfficerState(3, 2, 2, new(80, 70, 20, 90), false).WithLoyalty(2, 100))],
        Military = new(20, 3, Enumerable.Repeat(0, 60).ToImmutableArray(), [new(0, 1, null)],
            [new(7, 10, 10, 20000, 5, new(1, 1), TravelMode.Land)
                { Combat = new(20, 20, 50, 20, 50), Attack = new(1, true),
                  CaptureRanks = [BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle] },
             new(70, 10, 10, 20000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(92, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) },
             new(2, new(2, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) },
             new(3, new(27, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) }]) { TransportFormations = new(70, null) },
        Information = new([], [], [], [new CorpsState(1, 1, 1, new(100000, 100000)) { IsRoot = true },
            new CorpsState(2, 2, 2, new(100000, 100000)) { IsRoot = true }], [])
    };

    [Fact]
    public void HarborRouteSurvivesCandidateCommandRestoreAndActualMovement()
    {
        var original = Fixture();
        var definition = original with
        {
            Settlements = original.Settlements.Select(s => s with { X = s.Id == 3 ? 10 : s.X, Y = 0 })
                .Append(new(4, "harbor", 2, 3, 2, 3000, 100, 100, 100, [])).ToArray(),
            Military = original.Military! with
            {
                Height = 5,
                TerrainIds = Enumerable.Range(0, 100).Select(i => i / 20 > 0 && i % 20 is > 0 and < 10 ? 1 : 0).ToImmutableArray(),
                Terrains = [new(0, 1, null), new(1, null, 1) { IsWater = true }],
                Formations = original.Military.Formations.Select(f => f with { MovementBudget = 2 })
                    .Append(original.Military.Formations[0] with { Id = 8, MovementBudget = 20, TravelMode = TravelMode.Water }).ToImmutableArray(),
                BasicNavalFormationId = 8,
                Places = original.Military.Places.Select(p => p with { Position = new(p.Id == 3 ? 52 : p.Position.X, 2), FacilityTypeId = 10 })
                    .Append(new(4, new(17, 12), new(1, 1)) { FacilityTypeId = 9 }).ToImmutableArray(),
                Facilities = [new(9, 0, 100, new(1, 1), 0, false, true, true, 500000, null, null, null, []) { IsHarbor = true },
                    new(10, 0, 1000, new(1, 1), 0, false, true, true, 500000, null, null, null, [])]
            }
        };
        var session = GameSession.Start(definition, 42);
        var deploy = new DeployUnitCommand(Guid.Empty, 2, 0, 2, [3], 5000, 7,
            new(MilitaryOrderKind.AttackSettlement, null, 3)) { NavalFormationId = 8, Waypoint = new(16, 12) };
        session.EndControl(new("player"));
        var command = deploy with { CommandId = Guid.NewGuid(), Sequence = session.Snapshot.Simulation.NextSequence };
        var submitted = session.Execute(new("ai:2"), command);
        Assert.True(submitted.Submission.Accepted, submitted.Submission.Rejection.ToString());
        int unitId = Assert.Single(submitted.Events.OfType<UnitDeployedEvent>()).UnitId;
        Assert.Equal(command.Waypoint, session.Snapshot.Units.Single(u => u.Id == unitId).Waypoint);
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        Assert.Equal(command.Waypoint, restored.Snapshot.Units.Single(u => u.Id == unitId).Waypoint);
        bool reached = false;
        for (int xun = 0; xun < 8 && !reached; xun++)
        {
            var result = session.AdvanceXun();
            EventAssert.Equal(result.Events, restored.AdvanceXun().Events);
            reached |= result.Events.OfType<UnitWaypointReachedEvent>().Any(e => e.Unit.Id == unitId && e.Position == command.Waypoint);
        }
        Assert.True(reached);
    }

    [Fact]
    public void DeploymentPreparationMatchesSubmissionWithoutMovingTheArmy()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 190);
        var world = session.Snapshot;
        var engine = GameSession.EngineFactory(definition)(WorldState.Restore(world with
            { Simulation = world.Simulation with { ActiveControl = 1 } }));
        var command = new DeployUnitCommand(Guid.NewGuid(), 2, world.Simulation.NextSequence, 2, [3], 5000, 7,
            new(MilitaryOrderKind.AttackSettlement, null, 3));
        var before = engine.Snapshot;
        foreach (var query in new[] { command, command with { Soldiers = 50000 }, command with { OfficerIds = [1] },
            command with { DepartureDelayDays = 20 } })
        foreach (var context in new[] { new CommandContext("ai:2"), new CommandContext("forged") })
            Assert.Equal(engine.Validate(context, query).Rejection, engine.GetPreparationEligibility(context, query));
        StateAssert.Equal(before, engine.Snapshot);
        Assert.Equal(CommandRejection.None, engine.GetPreparationEligibility(new("ai:2"), command));
        Assert.True(engine.Execute(new("ai:2"), command).Submission.Accepted);
        Assert.NotEqual(CommandRejection.None, engine.GetPreparationEligibility(new("ai:2"), command with
            { CommandId = Guid.NewGuid(), Sequence = engine.Snapshot.Simulation.NextSequence }));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void ExplicitNavigationMatchesTheWorldForecastWithPreparationAndWaypoint(int preparation)
    {
        var basis = Fixture();
        var definition = basis with { Settlements = basis.Settlements.Select(s => s.Id == 3 ? s with { OwnerId = 2 } : s).ToArray() };
        var session = GameSession.Start(definition, 42);
        var rules = session.MilitaryRules!;
        var occupied = MilitaryRouteQuery.Occupancy(session.Snapshot, rules, 2, new(), includeUnits: false).ToImmutableArray();
        var start = rules.Place(session.Snapshot.Settlements.Single(s => s.Id == 2)).Position;
        var target = rules.SettlementArea(session.Snapshot.Settlements.Single(s => s.Id == 3));
        var via = new WorldPosition(12, 2);
        var explicitEstimate = NavigationArrivalForecast.Evaluate(new(rules.Travel(70, null), start, target, occupied,
            PreparationDays: preparation, Waypoint: via), new(100000));
        var existing = new MilitaryArrivalForecast(session.Snapshot, rules).EstimateDeployment(new(2, 70, null,
            new(MilitaryOrderKind.Transport, null, 3), new(), preparation, via));
        Assert.Equal(existing, explicitEstimate);
    }

}
