using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class MilitaryContentCompilerTests
{
    [Fact]
    public void KeepsEveryTerrainAndFormationAndSeparatesHardBlockingFromTerrain()
    {
        var squares = Enumerable.Range(0, 40000).Select(i =>
            new San9ScenarioSquareRecord(i, (ushort)i, 0, 0, 0, 0, 0, 3, 0, 0, 0, 0)).ToArray();
        squares[3] = squares[3] with { SquareFlags = 2, TerrainCode = 2 };
        squares[4] = squares[4] with { SquareFlags = 1024, TerrainCode = 15 };
        for (int i = 0; i < 17; i++) squares[2000 + i * 10] = squares[2000 + i * 10] with { TerrainCode = (byte)i };
        squares[4000] = squares[4000] with { TerrainCode = 1, OffsetCode = 2 };
        squares[4002] = squares[4002] with { OffsetCode = 2 };
        var terrains = Enumerable.Range(0, 17).Select(i => new San9ScenarioTerrainRecord(i, (ushort)i, 999, 99)).ToArray();
        var formations = Enumerable.Range(0, 28).Select(i => new San9ScenarioFormationRecord(i, (ushort)i, 0, 0, 0,
            (ushort)(i + 1), (ushort)(i + 2), (ushort)(i + 3), (ushort)(i + 4), 12, 1, 1, 100, 0, new byte[] { 1, 3, 4, 5, 7 }, 0)).ToArray();
        SettlementInitialState[] places = [new(5000, "city", 1, 10, 10, 100, 100, 100, 50, [])];
        formations[0] = formations[0] with { Mobility = 10 };
        formations[1] = formations[1] with { Mobility = 11 };
        formations[2] = formations[2] with { Mobility = 13 };
        var buildings = Enumerable.Range(0, 17).Select(i => new San9ScenarioFixedBuildingRecord(i, (ushort)i, 0, 0, 0, 600, 100, 5, 5, 0, 15, 0, 0)).ToArray();
        var tactics = Enumerable.Range(0, 41).Select(i => new San9ScenarioTacticRecord(i, (ushort)i,
            0, 0, 0, (ushort)(i >= 39 ? 9 : 0), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)).ToArray();
        tactics[12] = tactics[12] with { CompatibleArrayTypeMaskHigh = (1 << 5) | (1 << 10) };
        tactics[15] = tactics[15] with { CompatibleArrayTypeMaskLow = 1 << 8 };
        var definition = MilitaryContentCompiler.Compile(squares, terrains, formations, places, [], buildings, tactics, places.ToDictionary(p => p.Id, _ => System.Collections.Immutable.ImmutableArray<int>.Empty));
        Assert.Equal(27, definition.UnformedFormationId);
        Assert.Equal(8, definition.CityInfluence!.RadiusCells);
        Assert.Equal(5, definition.Geography.FootprintScale);
        Assert.Equal(10, definition.CityInfluence.LandTacticBonus);
        Assert.Equal(squares.Where(s => s.PathBlockingBits == 0x01fe).Select(s => new MapCell(s.GridX, s.GridY)), definition.Geography.BlockedCells);
        Assert.Equal(40000, definition.RegionIds.Length);
        Assert.Equal(40000, definition.TerritoryExits.Length);
        Assert.Equal(TerritoryExits.East | TerritoryExits.South | TerritoryExits.West, definition.TerritoryExits[3]);
        Assert.Equal(TerritoryExits.All, definition.TerritoryExits[4]);
        Assert.All(definition.RegionIds, id => Assert.Equal(0, id));
        var geographicSquares = squares.ToArray();
        geographicSquares[1] = geographicSquares[1] with { ZoneCode = 50 };
        geographicSquares[2] = geographicSquares[2] with { ZoneCode = ushort.MaxValue };
        San9ScenarioZoneRecord[] geographicZones = [new(0, 0, 0, ushort.MaxValue, 0, 0, ushort.MaxValue, 3, 0, 0, 0, 0)];
        var geography = MilitaryContentCompiler.Compile(geographicSquares, terrains, formations, places, geographicZones, buildings, tactics, places.ToDictionary(p => p.Id, _ => System.Collections.Immutable.ImmutableArray<int>.Empty));
        Assert.Equal(50, geography.RegionIds[1]);
        Assert.Null(geography.RegionIds[2]);
        var geographicRules = new MilitaryRulebook(geography);
        Assert.Equal(50, geographicRules.RegionAt(MapCoordinates.Center(new(1, 0))));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(geography with { RegionIds = [0] }));
        Assert.Equal(12, definition.Formations.Single(f => f.Id == 21).RequiredTacticId);
        Assert.Equal(12, definition.Formations.Single(f => f.Id == 26).RequiredTacticId);
        Assert.Equal(15, definition.Formations.Single(f => f.Id == 8).RequiredTacticId);
        Assert.Null(definition.Formations.Single(f => f.Id == 20).RequiredTacticId);
        Assert.Equal(TacticEffect.CounterTrap, definition.Tactics.Single(t => t.Id == 21).Effect);
        Assert.All(definition.Tactics, t => Assert.Equal(t.Id is >= 0 and <= 5 ? (int?)5 : null, t.ContactRange));
        Assert.Equal(new FacilityAttackModifier(true, 0), definition.Tactics.Single(t => t.Id == 39).FacilityAttack);
        Assert.Equal(new FacilityAttackModifier(false, 5), definition.Tactics.Single(t => t.Id == 40).FacilityAttack);
        Assert.All(definition.Tactics.Where(t => t.Id < 39), t => Assert.Null(t.FacilityAttack));
        Assert.Equal(TacticEffect.CounterConfusion, definition.Tactics.Single(t => t.Id == 22).Effect);
        Assert.True(definition.Tactics.Single(t => t.Id == 22).Selectable);
        Assert.Equal(Enumerable.Range(0, 17), definition.Terrains.Select(t => t.Id));
        Assert.Equal(Enumerable.Range(0, 28), definition.Formations.Select(f => f.Id));
        Assert.Equal(new MapCell(3, 0), Assert.Single(definition.BlockedCells));
        Assert.Equal(2, definition.TerrainIds[3]);
        Assert.Equal(15, definition.TerrainIds[4]);
        Assert.All(definition.Formations.Where(f => f.Id >= 3), f => Assert.Equal(60, f.MovementBudget));
        foreach (var formation in definition.Formations)
        {
            var source = formations.Single(f => f.Id == formation.Id);
            Assert.Equal(10, formation.MovementStepCost);
            Assert.Equal(source.Id == 1, formation.DoublesCapture);
            Assert.Equal(source.Id == 3, formation.DoublesDuelChance);
            Assert.Equal(source.Id == 11 ? new[] { 0 } : [], formation.MartialTacticVulnerabilities);
            Assert.Equal(new CombatProfile(source.AgainstUnits, source.AgainstSoldiers, source.AgainstWalls,
                source.DefensePower, source.Id == 7 ? 80 : 50), formation.Combat);
            Assert.Equal(source.Id == 27 ? null : new OrdinaryAttack(source.Id == 10 ? 15 : source.Id is 21 or 26 ? 5 : 10, true)
                { ElevationBonusPerLevel = source.Id < 20 ? 5 : 0, MaximumElevationBonus = source.Id < 20 ? 10 : 0,
                    RequiresWaterOrHarbor = source.Id is 21 or 26, RangedSiege = source.Id == 10 }, formation.Attack);
            Assert.Equal(source.Id is not (13 or 14), formation.GainsDailyCombatProficiency);
            Assert.Equal(source.Id == 7, formation.HalvesHostileStrategyChance);
            Assert.Equal(source.Id == 27, formation.ImmuneToDisorder);
            Assert.Equal(source.Id is >= 21 and <= 23 ? source.Id - 9 : (int?)null, formation.AutomaticTacticLearningId);
            Assert.Equal(source.Id is >= 8 and <= 11 ? source.Id + 7 : (int?)null, formation.CaptureTacticLearningId);
            Assert.Equal(source.Id is >= 20 and <= 26 ?
                [BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle] :
                new[] { BattleRank.Front, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Rear }, formation.CaptureRanks);
        }
        var rules = new MilitaryRulebook(definition);
        // Imported budgets must produce original ordinary daily distances in fine coordinates.
        foreach (var (id, distance) in new[] { (0, 5), (1, 6), (2, 7), (3, 6) })
        {
            var formation = rules.Formations[id];
            var movement = rules.Map.Advance(new(100, 100), new(160, 100), formation.Footprint,
                formation.TravelMode, [], formation.MovementBudget, formation.MovementStepCost);
            Assert.Equal(new WorldPosition(100 + distance, 100), movement.Position);
            Assert.False(movement.Arrived);
        }
        int?[] expectedTraps = [36, null, 35, 35, 35, 35, null, 35, 35, null, 37, null, null, null, null, 37, 37];
        for (int i = 0; i < 17; i++) Assert.Equal(expectedTraps[i], rules.TrapAt(MapCoordinates.Center(new((2000 + i * 10) % 200, (2000 + i * 10) / 200)))?.Id);
        Assert.Equal(38, rules.TrapAt(MapCoordinates.Center(new(3, 20)))?.Id);
        Assert.Equal(35, rules.TrapAt(MapCoordinates.Center(new(4, 20)))?.Id);
        Assert.Equal(38, rules.TrapAt(MapCoordinates.Center(new(3, 21)))?.Id);
        Assert.Equal(35, rules.TrapAt(MapCoordinates.Center(new(3, 23)))?.Id);
        Assert.Equal(35, rules.TrapAt(MapCoordinates.Center(new(2, 20)))?.Id);
        Assert.Equal(38, rules.TrapAt(MapCoordinates.Center(new(3, 20)))!.Id);
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { Elevations = [] }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { TrapTerrains = definition.TrapTerrains.SetItem(0, new(0, 24, false)) }));
        Assert.Null(rules.Map.PassageCost(MapCoordinates.Center(new(3, 0)), new(1, 1), TravelMode.Land, []));
        Assert.Equal(10, rules.Map.PassageCost(MapCoordinates.Center(new(4, 0)), new(1, 1), TravelMode.Water, []));
        Assert.Null(rules.Map.PassageCost(MapCoordinates.Center(new(4, 0)), new(1, 1), TravelMode.Land, []));
        Assert.Equal(5000, Assert.Single(definition.Places).Id);
        Assert.Equal(new CombatProfile(13, 14, 0, 16, 50), Assert.Single(definition.Places).Combat);
        Assert.All(definition.Facilities, facility => Assert.Equal(new CombatProfile(13, 14, 0, 16, 50), facility.Combat));
        Assert.False(Assert.Single(definition.Places).DestroyedAtZeroDurability);
        Assert.Equal(15, Assert.Single(definition.Places).WoundedRecovery);
        Assert.Throws<InvalidDataException>(() => MilitaryContentCompiler.Compile(squares, terrains[..16], formations, places, [], [], tactics, new Dictionary<int, System.Collections.Immutable.ImmutableArray<int>>()));
        Assert.Throws<InvalidDataException>(() => MilitaryContentCompiler.Compile(squares, terrains, formations[..27], places, [], [], tactics, new Dictionary<int, System.Collections.Immutable.ImmutableArray<int>>()));
        Assert.Throws<InvalidDataException>(() => MilitaryContentCompiler.Compile(squares, terrains, formations, places, [], buildings, tactics[..40], new Dictionary<int, System.Collections.Immutable.ImmutableArray<int>>()));
    }
}
