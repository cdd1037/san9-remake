using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Core.Tests;

public sealed class FixedPurposeContactTests
{
    private static MilitaryRulebook Rules(bool water = false, bool destroyed = false)
    {
        var combat = new CombatProfile(10, 10, 10, 10, 50);
        FormationRule Formation(int id, TravelMode mode) => new(id, 100, 0, 100000, 1, new(4, 4), mode)
        {
            Combat = combat, Attack = new(10, true),
            Positions = FormationTestData.Layout([BattleRank.Middle])
        };
        return new(new(60, 1, [.. Enumerable.Repeat(0, 60)], [new(0, 10, 10) { IsWater = water }],
            [Formation(100, TravelMode.Land), Formation(500, TravelMode.Land), Formation(501, TravelMode.Water),
             Formation(600, TravelMode.Land), Formation(601, TravelMode.Water)],
            [new(1, new(12, 2), new(1, 1)) { Combat = combat, FacilityTypeId = 9000 },
             new(2, new(42, 2), new(2, 1)) { Combat = combat, Attack = new(10, false), DestroyedAtZeroDurability = destroyed, FacilityTypeId = 9000 },
             new(3, new(202, 2), new(1, 1)) { Combat = combat, FacilityTypeId = 9000 },
             new(4, new(262, 2), new(1, 1)) { Combat = combat, FacilityTypeId = 9000 }])
        {
            ConstructionFormations = new(500, 501), TransportFormations = new(600, 601),
            Facilities = [new(9000, 0, 600, new(1, 1), 10, true, true, true, 100000, null, null, null, [])],
            ConstructionSites = [new(5, new(152, 2), [9000])]
        });
    }

    private static WorldState World(bool construction = false, int? servicePlace = 1, uint defenders = 100000, int durability = 1000) => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 21 }],
        [new(1, 1, 1000, 1000, 1000, 90, 0, 0, 100, 0, 100),
         new(2, 2, defenders, durability, 1000, 90, 0, 0, 100, 0, 100),
         new(3, 1, 1000, 1000, 1000, 90, 0, 0, 100, 0, 100),
         new(4, 2, 1000, 1000, 1000, 90, 0, 0, 100, 0, 100)],
        [new(11, 1, 1, new(80, 80, 80, 80), false) { Status = OfficerStatus.Ruler },
         new(12, 1, null, new(80, 80, 80, 80), false) { UnitId = 100 },
         new(21, 2, 4, new(80, 80, 80, 80), false) { Status = OfficerStatus.Ruler }],
        [new(100, 1, new(32, 2), 10000)
        {
            FormationId = construction ? 500 : 600, NavalFormationId = construction ? 501 : 601,
            CommanderId = 12, ServicePlaceId = servicePlace, Morale = 90,
            MemberPositions = FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty.Add(12, BattleRank.Middle)),
            Order = construction ? new(MilitaryOrderKind.Construct, null, null) { Construction = new(5, 9000, new(152, 2), 12) }
                : new(MilitaryOrderKind.Transport, null, 3),
            CargoMoney = construction ? 0u : 100u, CargoFood = construction ? 0u : 200u, Waypoint = new(172, 2)
        }],
        controls: [new("player", 1, ControllerKind.Player)],
        corps: [new(1, 1, 11, new(10000, 100000)) { IsRoot = true }, new(2, 2, 21, new(10000, 100000)) { IsRoot = true }]);

    private static SimulationEngine Engine(WorldState world, MilitaryRulebook rules) => new(world, EconomyRulebook.Empty,
        new([new(1, new(12, 2)), new(2, new(42, 2)), new(3, new(202, 2)), new(4, new(262, 2)), new(5, new(152, 2))],
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3, [4] = 4, [5] = 1 }), TreasureRulebook.Empty, military: rules);

    private static AdvanceXunResult AdvanceDay(SimulationEngine engine)
    {
        if (engine.Snapshot.Simulation.Boundary == SimulationBoundary.Stable && engine.Snapshot.Simulation.ActiveControl == 0)
            engine.EndControl(new("player"));
        return engine.AdvanceDay();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CollisionOpensOrdinaryCombatAcrossDaysAndRestore(bool construction, bool water)
    {
        var rules = Rules(water);
        var engine = Engine(World(construction), rules);
        var mission = engine.Snapshot.Units.Single();
        engine.EndControl(new("player"));
        var result = engine.AdvanceXun();
        var exchanges = result.Events.OfType<BattleDamageEvent>().GroupBy(e => e.Clock).ToArray();
        Assert.Equal(10, exchanges.Length);
        Assert.All(exchanges, hits =>
        {
            Assert.Contains(hits, e => e is { AttackerId: 100, AttackerIsSettlement: false, SettlementId: 2, TacticId: null });
            Assert.Contains(hits, e => e is { AttackerId: 2, AttackerIsSettlement: true, TargetUnitId: 100 });
        });
        var unit = Assert.Single(engine.Snapshot.Units);
        Assert.Equal(new MapObjectReference(2, true), unit.ContactTarget);
        Assert.Equal(mission.Order, unit.Order);
        Assert.Equal(mission.Waypoint, unit.Waypoint);
        Assert.Equal(mission.CargoMoney, unit.CargoMoney);
        Assert.Equal(mission.CargoFood, unit.CargoFood);
        Assert.Equal(ArrivalEstimateStatus.Known, new MilitaryArrivalForecast(engine.Snapshot, rules).Estimate(100).Status);
        var snapshot = JsonSerializer.Deserialize<WorldSnapshot>(JsonSerializer.Serialize(engine.CaptureStableState()))!;
        var restored = Engine(WorldState.Restore(snapshot), rules);
        var expected = AdvanceDay(engine);
        var actual = AdvanceDay(restored);
        StateAssert.Equal(expected.Snapshot, actual.Snapshot);
        Assert.Equal(CanonicalJson.Text(expected.Events), CanonicalJson.Text(actual.Events));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public void CapturedBuildingReceivesTransportAndOfficersFollowTheSavedMission(int? servicePlace)
    {
        var rules = Rules();
        var engine = Engine(World(servicePlace: servicePlace, defenders: 1, durability: 1), rules);
        var result = AdvanceDay(engine);
        Assert.Equal(1, engine.Snapshot.Settlements.Single(s => s.Id == 2).OwnerId);
        Assert.Empty(engine.Snapshot.Units);
        var absorption = Assert.Single(result.Events.OfType<UnitAbsorbedEvent>());
        Assert.Equal(2, absorption.SettlementId);
        Assert.True(absorption.WasTransport);
        Assert.Contains(result.Events, e => e is ResourceChangedEvent { Reason: "transport-unload", Account.Kind: ResourceOwnerKind.Corps, Resource: ResourceKind.Money, Actual: 100 });
        Assert.Contains(result.Events, e => e is ResourceChangedEvent { Reason: "transport-unload", Account.Kind: ResourceOwnerKind.Corps, Resource: ResourceKind.Food, Actual: 200 });
        if (servicePlace is null)
        {
            Assert.Empty(engine.Snapshot.Simulation.Tasks);
            Assert.Equal(2, engine.Snapshot.Officers.Single(p => p.Id == 12).SettlementId);
            return;
        }
        var returning = Assert.Single(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(PersonnelReturns.Kind, returning.Kind);
        Assert.Equal(1, returning.Journey!.DestinationPlaceId);
        int returnDays = returning.Journey.ReturnLeg!.CountdownDays;
        for (int day = 0; day < returnDays; day++) AdvanceDay(engine);
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(1, engine.Snapshot.Officers.Single(p => p.Id == 12).SettlementId);
    }

    [Fact]
    public void DestroyedObstacleReleasesContactAndTransportUnloadsAtItsOriginalDestination()
    {
        var rules = Rules(destroyed: true);
        var engine = Engine(World(durability: 1), rules);
        var mission = engine.Snapshot.Units.Single();
        var battle = AdvanceDay(engine);
        Assert.Contains(battle.Events, e => e is SettlementCapturedEvent { SettlementId: 2, Destroyed: true });
        var continuing = Assert.Single(engine.Snapshot.Units);
        Assert.Null(continuing.ContactTarget);
        Assert.Equal(mission.Order, continuing.Order);
        Assert.Equal(mission.Waypoint, continuing.Waypoint);
        var facts = new List<DomainEvent>();
        for (int day = 0; day < 30 && engine.Snapshot.Units.Count > 0; day++)
            facts.AddRange(AdvanceDay(engine).Events);
        Assert.Empty(engine.Snapshot.Units);
        var absorption = Assert.Single(facts.OfType<UnitAbsorbedEvent>());
        Assert.Equal(3, absorption.SettlementId);
        Assert.True(absorption.WasTransport);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnitCollisionDoesNotCreateContactAndRestoreRejectsUnitContacts(bool construction)
    {
        var rules = Rules();
        var state = World(construction);
        var mutation = state.BeginMutation();
        var unit = mutation.Units[100];
        mutation.AddUnit(new(200, 2, new(32, 2), 1000) { FormationId = 100, Morale = 90 });
        var collision = new MapOccupancy(new(32, 2), new(4, 4)) { Object = new(200, false) };
        Assert.Null(MilitaryContacts.AfterMovement(mutation, unit, collision, rules).ContactTarget);
        mutation.Replace(unit with { ContactTarget = collision.Object });
        state.Commit(mutation);
        Assert.Throws<ArgumentException>(() => Engine(WorldState.Restore(state.CreateSnapshot()), rules));
    }
}
