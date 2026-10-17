using San9.Tests;
using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class MilitaryRoutePreviewTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DeploymentCoarseRouteAndCombatApproachRespectTheirSeparateContracts(bool catapult, bool relay)
    {
        var basis = G01ScenarioTests.Fixture();
        var definition = basis with
        {
            Settlements = basis.Settlements.Select(s => s with { Soldiers = 3000, X = s.Id == 1 ? 2 : 30, Y = 10 }).ToArray(),
            Military = new(40, 20, Enumerable.Repeat(0, 800).ToImmutableArray(), [new(0, 10, null)],
                [new(8, 95, 0, 10000, 5, new(3, 3), TravelMode.Land)
                {
                    Combat = new(10, 10, 10, 10, 50),
                    CaptureRanks = [BattleRank.Front, BattleRank.Middle, BattleRank.Middle, BattleRank.Rear, BattleRank.Rear],
                    Attack = new(10, true) { RangedSiege = catapult, ElevationBonusPerLevel = 5, MaximumElevationBonus = 10 }
                }],
                [new(1, new(12, 52), new(3, 3)) { Combat = new(10, 10, 10, 10, 50), Attack = new(10, false) },
                 new(2, new(152, 52), new(3, 3)) { Combat = new(10, 10, 10, 10, 50), Attack = new(10, false) }])
            {
                DefaultDeploymentFormationId = 8,
                Elevations = Enumerable.Range(0, 800).Select(i => i % 40 < 30 ? 3 : 1).ToImmutableArray()
            },
            Information = basis.Information with { Formations = [new(8, "formation")] }
        };
        var session = GameSession.Start(definition, 42);
        var draft = new DeploymentDraft(session.View, 1);
        Assert.True(draft.SetMembers([1]));
        Assert.True(draft.SetSoldiers(1000));
        var targets = new DeploymentTargets(draft);
        var hash = session.Snapshot;
        Assert.True(targets.Select(new(2, false)));
        Assert.True(targets.Reachable);
        if (relay) Assert.True(targets.SetWaypoint(new(5, 12)));
        StateAssert.Equal(hash, session.Snapshot);
        var preview = targets.Route;
        Assert.True(targets.BeginPolicies());
        Assert.True(session.Execute(new("player"), targets.CreateCommand(Guid.NewGuid(), 0, true)).Submission.Accepted);
        var unit = Assert.Single(session.Snapshot.Units);
        var rules = session.View.MilitaryRules!;
        // Hold other objects and combat state fixed; this checks the map UI through the real movement use case.
        var world = WorldState.Restore(session.Snapshot).BeginMutation();
        UnitMovedEvent movement;
        int days = 0;
        do
        {
            var events = new List<DomainEvent>();
            MilitaryMovement.AdvanceDay(world, rules, new([]), PersonnelRulebook.Empty, events);
            movement = Assert.Single(events.OfType<UnitMovedEvent>());
            Assert.True(++days < 100);
        } while (movement.Status != NavigationStatus.Arrived);
        Assert.Equal(MapCoordinates.Cell(unit.Position), preview[0]);
        Assert.Equal(new MapCell(30, 10), preview[^1]);
        if (relay) Assert.Contains(new MapCell(5, 12), preview);
        Assert.All(preview.Zip(preview.Skip(1)), pair =>
            Assert.Equal(1, Math.Abs(pair.First.X - pair.Second.X) + Math.Abs(pair.First.Y - pair.Second.Y)));
        var source = MilitaryCombat.Area(world.Units[unit.Id], rules);
        var target = rules.SettlementArea(world.Settlements[2]);
        Assert.False(source.Overlaps(target));
        Assert.Equal(!catapult, source.IntersectsExpanded(target, 1));

        var orderView = new StrategySnapshot(definition, session.Snapshot);
        var redirect = new UnitCommandTargets(orderView, unit.Id, UnitCommandKind.Attack);
        Assert.Equal(preview.ToArray(), redirect.Route.ToArray());

        var contacted = session.Snapshot with
        {
            Units = [unit with { Position = new(82, 52), ContactTarget = new(2, true), CommandedThisXun = false }]
        };
        var march = new UnitCommandTargets(new(definition, contacted), unit.Id, UnitCommandKind.March);
        Assert.True(march.Select(new(1, false)));
        Assert.Equal(new MapCell(2, 10), march.Route[^1]);
        Assert.Equal(ArrivalEstimateStatus.Known, march.EstimateArrival().Status);
        Assert.Equal(new MapObjectReference(2, true), contacted.Units[0].ContactTarget);
    }
}
