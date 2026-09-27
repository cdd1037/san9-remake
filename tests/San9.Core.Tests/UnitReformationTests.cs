using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class UnitReformationTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 4)]
    [InlineData(3, 3)]
    public void EntrancesCoverEveryNonOverlappingContactIncludingOffCenterFootprints(int width, int height)
    {
        var place = new BattlefieldPlace(1, new(10, 10), new(2, 3));
        var footprint = new Footprint(width, height);
        var area = new MapOccupancy(place.Position, place.Footprint);
        var expected = from x in Enumerable.Range(0, 20) from y in Enumerable.Range(0, 20)
            let candidate = new MapOccupancy(new(x, y), footprint)
            where !candidate.Overlaps(area) && candidate.DistanceTo(area) == 1
            select candidate.Position;
        Assert.Equal(expected.OrderBy(p => p.X).ThenBy(p => p.Y), area.Entrances(footprint).OrderBy(p => p.X).ThenBy(p => p.Y));
    }

    private static readonly CommandContext Player = new("player");
    private static MilitaryRulebook Rules() => new(new(6, 1, [0, 2, 1, 1, 2, 0],
        [new(0, 1, null), new(1, null, 1) { IsWater = true }, new(2, 1, 1)],
        [new(10, 5, 0, 10000, 2, new(1, 1), TravelMode.Land) { CaptureRanks = [BattleRank.Front, BattleRank.Rear] },
         new(20, 5, 50, 10000, 2, new(1, 1), TravelMode.Water) { CaptureRanks = [BattleRank.Middle, BattleRank.Middle] },
         new(70, 5, 0, 10000, 2, new(1, 1), TravelMode.Land) { CaptureRanks = [BattleRank.Front, BattleRank.Rear] },
         new(71, 5, 50, 10000, 2, new(1, 1), TravelMode.Water) { CaptureRanks = [BattleRank.Middle, BattleRank.Middle] }],
        [new(1, new(4, 2), new(1, 1)), new(2, new(25, 2), new(1, 1))]) { TransportFormations = new(70, 71) });

    private static WorldState World(int x = 7, bool deployed = true) => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 0)],
        [new(1, 1, 1000, 100, 100, 70, 0, 0, 100, 0, 100), new(2, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        [new(11, 1, deployed ? null : 1, new(70, 70, 70, 70), false) { UnitId = deployed ? 0 : null },
         new(12, 1, deployed ? null : 1, new(70, 70, 70, 70), false) { UnitId = deployed ? 0 : null }],
        units: deployed ? [new(0, 1, new(x, 2), 500) { FormationId = 10, Morale = 70, Wounded = 80, CargoFood = 100,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front).Add(12, BattleRank.Rear) }] : [],
        controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 10000)) { IsRoot = true }]);

    private static SimulationEngine Engine(WorldState world) => new(world, EconomyRulebook.Empty,
        PersonnelRulebook.Empty, TreasureRulebook.Empty, military: Rules());
    private static ReformUnitCommand Reform(SimulationEngine engine, int formation, int[]? people = null, MilitaryOrder? order = null) =>
        new(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, 0, formation, (people ?? [12, 11]).ToImmutableArray(), order);

    [Fact]
    public void EmbarkCrossWaterAndDisembarkPreservesMembersWoundedAndCargo()
    {
        var engine = Engine(World());
        uint money = engine.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money;
        Assert.True(engine.Execute(Player, Reform(engine, 20, order: new(MilitaryOrderKind.Move, new(22, 2), null))).Submission.Accepted);
        var boat = Assert.Single(engine.Snapshot.Units);
        Assert.All(boat.MemberRanks.Values, rank => Assert.Equal(BattleRank.Middle, rank));
        Assert.Equal(money - 50, engine.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money);
        Assert.Equal(80u, boat.Wounded);
        Assert.Equal(100u, boat.CargoFood);
        engine.EndControl(Player);
        engine.AdvanceXun();
        Assert.Equal(new WorldPosition(22, 2), Assert.Single(engine.Snapshot.Units).Position);
        Assert.True(engine.Execute(Player, Reform(engine, 10, order: new(MilitaryOrderKind.Return, null, 2))).Submission.Accepted);
        Assert.Equal(BattleRank.Front, Assert.Single(engine.Snapshot.Units).MemberRanks[12]);
        engine.EndControl(Player);
        var result = engine.AdvanceXun();
        Assert.Single(result.Events.OfType<UnitAbsorbedEvent>());
        Assert.Empty(engine.Snapshot.Units);
        Assert.All(engine.Snapshot.Officers, p => Assert.Equal(2, p.SettlementId));
        Assert.Equal(80u, engine.Snapshot.Settlements.Single(s => s.Id == 2).Wounded);
    }

    [Theory]
    [InlineData(2, 20, false)]
    [InlineData(7, 999, false)]
    [InlineData(7, 20, true)]
    public void TerrainUnknownFormationAndMembershipRejectionsAreAtomic(int x, int formation, bool invalidMembers)
    {
        var engine = Engine(World(x));
        string before = engine.Snapshot.StateHash;
        Assert.False(engine.Execute(Player, Reform(engine, formation, invalidMembers ? [11, 11] : null)).Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }

    [Fact]
    public void WaterTransportUsesShoreEntrancesWithoutEnteringLandBuildingCenter()
    {
        var engine = Engine(World(deployed: false));
        var command = new TransportUnitCommand(Guid.NewGuid(), 1, 0, 1, [11], 500, 2, 10, 100);
        Assert.True(engine.Execute(Player, command).Submission.Accepted);
        Assert.Equal(new WorldPosition(4, 2), Assert.Single(engine.Snapshot.Units).Position);
        engine.EndControl(Player);
        var result = engine.AdvanceXun();
        Assert.Single(result.Events.OfType<UnitAbsorbedEvent>());
        Assert.Equal(new WorldPosition(24, 2), result.Events.OfType<UnitMovedEvent>().Last().To);
        Assert.Equal(500u, engine.Snapshot.Settlements.Single(s => s.Id == 2).Soldiers);
    }
}
