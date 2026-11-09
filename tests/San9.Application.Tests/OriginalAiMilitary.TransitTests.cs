using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed partial class OriginalAiMilitaryTests
{
    [Fact]
    public void InsufficientAttackStrengthUsesTheCompletedReferenceWithoutPublicRouteSearches()
    {
        var basis = Fixture();
        var content = basis with { Settlements = basis.Settlements.Select(s => s.Id == 2
            ? s with { Soldiers = 200000 } : s).ToArray() };
        var h = new Harness(content);
        h.TerrainRoutes.TransitNetwork(h.Rules, h.Engine.Snapshot);
        var report = h.Run(OriginalAiWork.Attack);
        Assert.Equal(0, report.Submitted);
        Assert.Equal(0, report.PathSearches);
        Assert.Empty(h.Commands);
    }

    private static ScenarioInitialState HarborTransitFixture(bool contact = true, int movementBudget = 10)
    {
        var basis = Fixture();
        return basis with
        {
            Settlements = [basis.Settlements[0] with { Soldiers = 0, Morale = 0 },
                basis.Settlements[1] with { Soldiers = 250000 },
                basis.Settlements[0] with { Id = 3, X = 3, Soldiers = 190854, Kind = SettlementKind.Facility },
                basis.Settlements[1] with { Id = 4, X = 7, Soldiers = 3000, Kind = SettlementKind.Facility }],
            Officers = basis.Officers.Select(p => p.State.OwnerId == 1
                ? p with { State = p.State with { SettlementId = 3 } } : p).ToArray(),
            CityConnections = [],
            RegionCityIds = new() { [1] = 1, [2] = 2, [3] = 1, [4] = 2, [5] = 1 },
            RegionNameKeys = new() { [1] = "home", [2] = "enemy-city", [3] = "source-port", [4] = "enemy-port", [5] = "gap" },
            UnbuiltPlaces = contact ? [] : [new(5, new(27, 7))],
            Military = basis.Military! with
            {
                Formations = basis.Military.Formations.Select(f => f with { MovementBudget = movementBudget }).ToImmutableArray(),
                Places = basis.Military.Places.Add(new(3, new(17, 7), new(1, 1)) { Combat = basis.Military.Places[0].Combat })
                    .Add(new(4, new(37, 7), new(1, 1)) { Combat = basis.Military.Places[1].Combat }),
                RegionIds = Enumerable.Range(0, 60).Select(i => (int?)(i % 20 < 2 ? 1 :
                    i % 20 < (contact ? 6 : 4) ? 3 : !contact && i % 20 < 6 ? 5 : i % 20 < 10 ? 4 : 2)).ToImmutableArray()
            }
        };
    }

    [Fact]
    public void HarborAttacksItsNearbyEnemyWithoutAnyMilitaryCityConnection()
    {
        var content = HarborTransitFixture();
        var h = new Harness(content);
        Assert.Equal(1, h.Run(OriginalAiWork.Attack).Submitted);
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        Assert.Equal(3, command.SettlementId);
        Assert.Equal(4, command.Order.SettlementId);
        Assert.Equal(190854u - command.Soldiers, h.Engine.Snapshot.Settlements.Single(s => s.Id == 3).Soldiers);
    }

    [Fact]
    public void InaccessiblePreferredCityLeavesTheBlockingPortAsAnIndependentTarget()
    {
        var basis = HarborTransitFixture();
        var content = basis with
        {
            Settlements = basis.Settlements.Select(s => s.Id == 2 ? s with { Soldiers = 1000 } : s).ToArray(),
            Military = basis.Military! with { Places = basis.Military.Places.Select(p => p.Id == 4
                ? p with { Footprint = new(1, 15) } : p).ToImmutableArray() }
        };
        var h = new Harness(content);
        Assert.Equal(1, h.Military.Run(OriginalAiWork.Attack, 20, h.State,
            new(1, OriginalAiMode.TargetSettlement, null, 2)).Submitted);
        var command = Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands));
        Assert.Equal(3, command.SettlementId);
        Assert.Equal(4, command.Order.SettlementId);
    }

    [Theory]
    [InlineData(3000, 4)]
    [InlineData(500000, 6)]
    public void PreferredCitysInterveningFacilityPrecedesEmptyCitiesAndFailureStillAllowsFallback(int defenders, int expectedTarget)
    {
        var basis = HarborTransitFixture();
        var content = basis with
        {
            Settlements = [.. basis.Settlements.Select(s => s.Id == 4 ? s with { Soldiers = (uint)defenders } : s),
                basis.Settlements[0] with { Id = 6, OwnerId = null, X = 3, Y = 9, Soldiers = 0, Morale = 0 }],
            RegionCityIds = new(basis.RegionCityIds) { [6] = 6 },
            RegionNameKeys = new(basis.RegionNameKeys) { [6] = "empty-fallback" },
            Military = basis.Military! with
            {
                Height = 12,
                TerrainIds = Enumerable.Repeat(0, 240).ToImmutableArray(),
                TerritoryExits = Enumerable.Repeat(TerritoryExits.All, 240).ToImmutableArray(),
                RegionIds = Enumerable.Range(0, 240).Select(i => (int?)(i % 20 < 6 && i / 20 > 5 ? 6 :
                    i % 20 < 2 ? 1 : i % 20 < 6 ? 3 : i % 20 < 10 ? 4 : 2)).ToImmutableArray(),
                Places = basis.Military.Places.Add(new(6, new(17, 47), new(1, 1)) { Combat = basis.Military.Places[0].Combat })
            }
        };
        var h = new Harness(content);
        var report = h.Military.Run(OriginalAiWork.Attack, 20, h.State,
            new(1, OriginalAiMode.TargetSettlement, null, 2));
        Assert.True(report.Submitted > 0);
        // Incoming hostile reserves now affect the neutral target's demand too.
        Assert.All(h.Commands, c =>
        {
            var command = Assert.IsType<DeployUnitCommand>(c);
            Assert.Equal(3, command.SettlementId);
            Assert.Equal(expectedTarget, command.Order.SettlementId);
        });
    }

    [Fact]
    public void AutomaticControlAndExactRestoreMakeTheSameHarborDeployment()
    {
        var content = HarborTransitFixture();
        var session = GameSession.Start(content, 42, playerForceController: ControllerKind.Automatic);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        var expected = session.AdvanceXun();
        var actual = restored.AdvanceXun();
        EventAssert.Equal(expected.Events, actual.Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Contains(expected.Events.OfType<UnitDeployedEvent>(), e => e.SettlementId == 3);
    }

    [Fact]
    public void TransitNetworkIsBuiltOnFirstAiUseAndReusedAcrossOperationsAndTurns()
    {
        var content = HarborTransitFixture();
        var session = GameSession.Start(content, 42, playerForceController: ControllerKind.Automatic);
        Assert.Null(session.TransitNetwork);
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(new PkSettingsOperation(true, true, false))).Disposition);
        Assert.Null(session.TransitNetwork);
        session.AdvanceXun();
        var network = Assert.IsType<SettlementTransitNetwork>(session.TransitNetwork);
        Assert.Equal(4, network.Nodes.Length);
        Assert.Equal(network.Nodes.Length, network.BuildSearches);
        Assert.NotNull(network.Find(3, 4));
        Assert.Contains(network.To(4), r => r.SourceId == 3);
        Assert.Equal(SessionDisposition.Accepted, session.Apply(session.Request(new PkSettingsOperation(true, true, false))).Disposition);
        Assert.Same(network, session.TransitNetwork);
        session.AdvanceXun();
        Assert.Same(network, session.TransitNetwork);
        session.AdvanceXun();
        Assert.Same(network, session.TransitNetwork);
        var restored = SaveService.Decode(SaveService.Encode(session), content);
        Assert.Null(restored.TransitNetwork);
        restored.AdvanceXun();
        var restoredNetwork = Assert.IsType<SettlementTransitNetwork>(restored.TransitNetwork);
        Assert.NotSame(network, restoredNetwork);
        Assert.Equal(network.Nodes.ToArray(), restoredNetwork.Nodes.ToArray());
        Assert.Equal(restoredNetwork.Nodes.Length, restoredNetwork.BuildSearches);
        Assert.Equal(network.From(3).Select(r => r.Reference.Days), restoredNetwork.From(3).Select(r => r.Reference.Days));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void OrdinaryEnemyFacilityNeedsFrontContactButAnExplicitTargetKeepsItsOwnStage(bool explicitTarget, bool expected)
    {
        var basis = HarborTransitFixture(contact: false);
        // Isolate the ordinary-facility stage: the city beyond the port has no reference route.
        var content = basis with { Military = basis.Military! with
            { BlockedCells = Enumerable.Range(0, 3).Select(y => new MapCell(10, y)).ToImmutableArray() } };
        var h = new Harness(content);
        var report = explicitTarget
            ? h.Military.Run(OriginalAiWork.Attack, 20, h.State, new(1, OriginalAiMode.TargetSettlement, null, 4))
            : h.Run(OriginalAiWork.Attack);
        Assert.Equal(expected ? 1 : 0, report.Submitted);
        if (expected) Assert.Equal(4, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Order.SettlementId);
        else Assert.Empty(h.Commands);
    }

    [Fact]
    public void SameCityAffiliationAndFrontContactDoNotBypassTheTimeLimit()
    {
        var basis = HarborTransitFixture(movementBudget: 1);
        var content = basis with
        {
            RegionCityIds = new(basis.RegionCityIds) { [4] = 1 },
            Military = basis.Military! with { Terrains = [new(0, 5, null)] }
        };
        var h = new Harness(content);
        Assert.Equal(0, h.Run(OriginalAiWork.Attack).Submitted);
        Assert.Empty(h.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryAttackStaysWithinTheReferenceNetworkEvenWhenActualMovementIsFastEnough(bool explicitTarget)
    {
        var basis = HarborTransitFixture(movementBudget: 1000);
        var content = basis with { Military = basis.Military! with { Terrains = [new(0, 200, null)] } };
        var h = new Harness(content);
        var rules = new MilitaryRulebook(content.Military);
        var traffic = SettlementTransitNetwork.Build(rules, h.Engine.Snapshot.Settlements);
        Assert.Contains(traffic.Nodes, n => n.Id == 4);
        Assert.Null(traffic.Find(3, 4));
        var navigation = new MilitaryRouteQueryContext(h.Engine.Snapshot, rules);
        var source = h.Engine.Snapshot.Settlements.Single(s => s.Id == 3);
        var start = navigation.DeploymentPosition(source, 400, null, 1, new());
        Assert.NotNull(start);
        var estimate = navigation.Estimate(new(0, 1, start.Value, 10000)
            { FormationId = 400, Order = new(MilitaryOrderKind.AttackSettlement, null, 4) }, null);
        Assert.Equal(RouteReachability.Reachable, estimate.Status);
        Assert.InRange(Assert.IsType<int>(estimate.Days), 1, 60);

        // Warm the graph so any subsequent search would be an out-of-scope AI query.
        Assert.Equal(0, h.Run(OriginalAiWork.Attack).Submitted);
        var report = explicitTarget
            ? h.Military.Run(OriginalAiWork.Attack, 20, h.State, new(1, OriginalAiMode.TargetSettlement, null, 4))
            : h.Run(OriginalAiWork.Attack);
        Assert.Equal(0, report.Submitted);
        Assert.Equal(0, report.PathSearches);
        Assert.Empty(h.Commands);
    }

    [Theory]
    [InlineData(10, 1)]
    [InlineData(1, 0)]
    public void FinalEquipmentRechecksTimeAfterTheReferenceFormationWasFastEnough(int selectedSpeed, int expected)
    {
        var basis = HarborTransitFixture();
        var content = basis with { Military = basis.Military! with
        {
            Formations = basis.Military.Formations.Add(basis.Military.Formations[0] with
                { Id = 600, MovementBudget = selectedSpeed, Combat = new(30, 500, 500, 30, 10) }),
            Terrains = [new(0, 4, null)]
        } };
        var h = new Harness(content);
        Assert.Equal(expected, h.Run(OriginalAiWork.Attack).Submitted);
        if (expected > 0) Assert.Equal(600, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).FormationId);
        else Assert.Empty(h.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryAttackDoesNotRequireADailyTraversalOfThePassBeforeDeparture(bool wide)
    {
        var basis = HarborTransitFixture();
        var content = basis with
        {
            Settlements = [.. basis.Settlements.Select(s => s with { Y = 2 }),
                basis.Settlements[0] with { Id = 6, X = 5, Y = 1, Soldiers = 0, Morale = 0, Kind = SettlementKind.Facility, CanHostPersons = false },
                basis.Settlements[0] with { Id = 7, X = 5, Y = 4, Soldiers = 0, Morale = 0, Kind = SettlementKind.Facility, CanHostPersons = false }],
            RegionCityIds = new(basis.RegionCityIds) { [6] = 1, [7] = 1 },
            RegionNameKeys = new(basis.RegionNameKeys) { [6] = "upper-work", [7] = "lower-work" },
            Military = basis.Military! with
            {
                Height = 5,
                TerrainIds = Enumerable.Repeat(0, 100).ToImmutableArray(),
                RegionIds = Enumerable.Range(0, 100).Select(i => (int?)(i % 20 < 2 ? 1 : i % 20 < 6 ? 3 : i % 20 < 10 ? 4 : 2)).ToImmutableArray(),
                TerritoryExits = Enumerable.Repeat(TerritoryExits.All, 100).ToImmutableArray(),
                Facilities = [new(700, 0, 100, new(5, 15), 0, false, false, false, 1000, null, null, null, []) { IsWork = true },
                    new(701, 0, 100, new(5, 5), 0, false, false, false, 1000, null, null, null, []) { IsWork = true },
                    new(702, 0, 100, new(1, 1), 0, false, true, true, 500000, null, null, null, [])],
                Places = basis.Military.Places.Select(p => p with { Position = p.Position with { Y = 12 }, FacilityTypeId = 702 }).ToImmutableArray()
                    .Add(new(6, new(27, 7), new(5, 15)) { FacilityTypeId = 700 })
                    .Add(new(7, new(27, 22), new(5, 5)) { FacilityTypeId = 701 }),
                Formations = basis.Military.Formations.Select(f => f with
                    { SoldierFootprints = wide ? [new(20000, new(7, 7))] : [] }).ToImmutableArray()
            }
        };
        var h = new Harness(content);
        Assert.Equal(1, h.Run(OriginalAiWork.Attack).Submitted);
        Assert.Equal(4, Assert.IsType<DeployUnitCommand>(Assert.Single(h.Commands)).Order.SettlementId);
    }

    [Fact]
    public void RejectedAttackLeavesTheWorldUnchangedAndWarmReferencesMakeTheSameDecision()
    {
        var h = new Harness(HarborTransitFixture(), reject: true);
        var before = h.Engine.Snapshot;
        var cold = h.Run(OriginalAiWork.Attack);
        var warm = h.Run(OriginalAiWork.Attack);
        Assert.Equal(1, cold.Rejected);
        Assert.Equal(cold.Rejected, warm.Rejected);
        Assert.Equal(0, warm.Submitted);
        StateAssert.Equal(before, h.Engine.Snapshot);
        var commands = h.Commands.Cast<DeployUnitCommand>().ToArray();
        Assert.Equal(2, commands.Length);
        Assert.Equal(commands[0].Soldiers, commands[1].Soldiers);
        Assert.Equal(commands[0].Order, commands[1].Order);
        Assert.Equal(commands[0].OfficerIds.ToArray(), commands[1].OfficerIds.ToArray());
        Assert.Equal(commands[0].FormationId, commands[1].FormationId);
        Assert.True(warm.PathSearches < cold.PathSearches);
    }
}
