using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class UnitDisplacementTests
{
    private static (WorldMutation World, SimulationRules Rules) Scenario(Func<MilitaryDefinition, MilitaryDefinition>? configure = null)
    {
        SettlementState Place(int id, int owner, uint soldiers) => new(id, owner, soldiers, 100, 100, soldiers > 0 ? 70 : 0,
            500, 100, 1000, 100, 1000) { Kind = SettlementKind.City, CorpsId = owner, ConsumesFood = false };
        var unit = new UnitState(11, 1, new(52, 52), 1000)
        {
            CorpsId = 1, ServicePlaceId = 10, FormationId = 1, Morale = 70, Wounded = 150,
            Order = new(MilitaryOrderKind.Move, new(7, 52), null), OrderClass = UnitOrderClass.Military,
            Lookahead = new(new(22, 52)), MovementRemainder = 1, CommanderId = 111
        };
        var world = WorldState.Create(new(190, 1, 1), 17,
            [new(1, 100) { LeaderId = 101 }, new(2, 100) { LeaderId = 201 }],
            [Place(10, 1, 0), Place(20, 1, 100), Place(30, 2, 100)],
            [new(101, 1, 20, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler },
             new(201, 2, null, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler, UnitId = 21 },
             new(111, 1, null, new(70, 70, 70, 70), false) { UnitId = 11 },
             new(112, 1, null, new(70, 70, 70, 70), false) { UnitId = 12 }],
            units: [unit, unit with { Id = 12, Position = new(45, 52), Wounded = 200, CommanderId = 112 },
                new(21, 2, new(67, 52), 1000) { CorpsId = 2, ServicePlaceId = 30, FormationId = 1, Morale = 70,
                    CommanderId = 201, Order = new(MilitaryOrderKind.AttackSettlement, null, 10) }],
            corps: [new(1, 1, 101, new(1000, 1000)) { IsRoot = true }, new(2, 2, 201, new(1000, 1000)) { IsRoot = true }]).BeginMutation();
        var definition = new MilitaryDefinition(30, 20, new int[600].ToImmutableArray(), [new(0, 10, null)],
            [new(1, 100, 0, 10000, 5, new(4, 4), TravelMode.Land)],
            [new(10, new(52, 52), new(25, 25)), new(20, new(12, 52), new(5, 5)), new(30, new(137, 52), new(5, 5))]);
        var rules = new SimulationRules
        {
            Personnel = new([new(10, new(52, 52)), new(20, new(12, 52)), new(30, new(137, 52))]),
            Military = new(configure?.Invoke(definition) ?? definition)
        };
        return (world, rules);
    }

    [Fact]
    public void CaptureDisplacesSurvivingFieldUnitsAndTheyCanContinueTheirOrders()
    {
        var (world, rules) = Scenario();
        var originals = world.Units.Values.Where(u => u.OwnerId == 1).ToArray();
        List<DomainEvent> events = [];
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(21, false), rules, events);
        Assert.Equal(2, world.Settlements[10].OwnerId);
        Assert.False(world.Forces[1].IsEliminated);
        Assert.Equal(new[] { 11, 12 }, events.OfType<UnitDisplacedEvent>().Select(e => e.UnitId));
        foreach (var original in originals)
        {
            var unit = world.Units[original.Id];
            Assert.False(MilitaryCombat.Area(unit, rules.Military!).Overlaps(rules.Military!.SettlementArea(world.Settlements[10])));
            Assert.Equal(original.Soldiers, unit.Soldiers);
            Assert.Equal(original.Wounded, unit.Wounded);
            Assert.Equal(original.Order, unit.Order);
            Assert.Equal(original.CommanderId, unit.CommanderId);
            Assert.Equal(unit.Id, world.Officers[unit.CommanderId!.Value].UnitId);
            Assert.Equal(1, world.Officers[unit.CommanderId!.Value].OwnerId);
            Assert.Equal(20, unit.ServicePlaceId);
            Assert.Null(unit.Lookahead);
            var moved = MilitaryNavigation.Advance(world, unit, unit.Id, rules.Military!, 100);
            Assert.NotEqual(unit.Position, moved.Position);
        }
        var fact = events.OfType<UnitDisplacedEvent>().First();
        Assert.Equal(fact, JsonSerializer.Deserialize<DomainEvent>(JsonSerializer.Serialize<DomainEvent>(fact)));
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void DefectionUsesTheSamePositionTransition()
    {
        var (world, rules) = Scenario();
        List<DomainEvent> events = [];
        SettlementAffiliation.Transfer(world, world.Settlements[10], 2, null, [], SettlementDefectionKind.Isolated, rules, events);
        Assert.Equal(2, events.OfType<UnitDisplacedEvent>().Count());
        Assert.All(world.Units.Values.Where(u => u.OwnerId == 1), u =>
            Assert.False(MilitaryCombat.Area(u, rules.Military!).Overlaps(rules.Military!.SettlementArea(world.Settlements[10]))));
    }

    [Theory]
    [InlineData("terrain")]
    [InlineData("building")]
    [InlineData("destination")]
    public void MissingExitLeavesUnitsInPlaceWithoutCrossingAnotherObstacle(string obstruction)
    {
        var (world, rules) = Scenario(d => obstruction switch
        {
            "terrain" => d with { BlockedCells = Enumerable.Range(0, 20).Select(y => new MapCell(7, y)).ToImmutableArray() },
            "building" => d with { Places = d.Places.Select(p => p.Id == 30 ? p with
                { Position = new(37, 50), Footprint = new(1, 100) } : p).ToImmutableArray() },
            _ => d
        });
        world.Replace(world.Settlements[10] with { OwnerId = 2, CorpsId = 2 });
        if (obstruction == "destination") world.Replace(world.Settlements[20] with { OwnerId = 2, CorpsId = 2 });
        List<DomainEvent> events = [];
        UnitDisplacement.Resolve(world, 10, rules.Military!, events);
        Assert.Empty(events);
        Assert.Equal(new WorldPosition(52, 52), world.Units[11].Position);
    }

    [Fact]
    public void AmphibiousDisplacementUsesTheFootprintAndFormationAtTheExit()
    {
        var (world, rules) = Scenario(d => d with
        {
            TerrainIds = Enumerable.Range(0, 600).Select(i => i % 30 >= 8 ? 1 : 0).ToImmutableArray(),
            Terrains = [new(0, 10, null), new(1, null, 10) { IsWater = true }],
            Formations = [.. d.Formations, new(2, 100, 0, 10000, 5, new(8, 8), TravelMode.Water)]
        });
        foreach (var unit in world.Units.Values.Where(u => u.OwnerId == 1).ToArray())
            world.Replace(unit with { NavalFormationId = 2 });
        world.Replace(world.Settlements[10] with { OwnerId = 2, CorpsId = 2 });
        List<DomainEvent> events = [];
        UnitDisplacement.Resolve(world, 10, rules.Military!, events);
        Assert.Equal(2, events.OfType<UnitDisplacedEvent>().Count());
        foreach (var unit in world.Units.Values.Where(u => u.OwnerId == 1))
        {
            Assert.Equal(1, rules.Military!.Formation(unit).Id);
            Assert.False(MilitaryCombat.Area(unit, rules.Military!).Overlaps(rules.Military.SettlementArea(world.Settlements[10])));
            Assert.NotNull(rules.Military.Travel(unit).Map.PassageCost(unit.Position, new(8, 8), TravelMode.Land,
                world.MovementOccupancy(rules.Military, 1, unit.Policies, unit.Id, null)));
        }
    }

    [Fact]
    public void LastCityCaptureEliminatesTheForceBeforeAnyDisplacement()
    {
        var (world, rules) = Scenario();
        world.Replace(world.Settlements[20] with { OwnerId = 2, CorpsId = 2 });
        world.Replace(world.Officers[101] with { SettlementId = null, UnitId = 11 });
        List<DomainEvent> events = [];
        BattleAftermath.ResolveSettlementDefeat(world, 10, new(21, false), rules, events);
        Assert.True(world.Forces[1].IsEliminated);
        Assert.DoesNotContain(world.Units.Values, u => u.OwnerId == 1);
        Assert.DoesNotContain(events, e => e is UnitDisplacedEvent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FriendlyOrNonHostileOwnershipDoesNotDisplaceUnits(bool friendlyOther)
    {
        var (world, rules) = Scenario();
        if (friendlyOther)
        {
            world.Replace(world.Settlements[10] with { OwnerId = 2, CorpsId = 2 });
            world.SetRelation(new(1, 2, 60));
        }
        List<DomainEvent> events = [];
        UnitDisplacement.Resolve(world, 10, rules.Military!, events);
        Assert.Empty(events);
        Assert.Equal(new WorldPosition(52, 52), world.Units[11].Position);
    }
}
