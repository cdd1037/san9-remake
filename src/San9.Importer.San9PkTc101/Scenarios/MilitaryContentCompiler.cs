using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Importer.San9PkTc101.Scenarios;

/// <summary>TC101 identities are interpreted here; domain movement consumes explicit passage and budgets.</summary>
public static class MilitaryContentCompiler
{
    public static MilitaryDefinition Compile(IReadOnlyList<San9ScenarioSquareRecord> squares,
        IReadOnlyList<San9ScenarioTerrainRecord> terrains, IReadOnlyList<San9ScenarioFormationRecord> formations,
        IReadOnlyList<SettlementInitialState> settlements, IReadOnlyList<San9ScenarioZoneRecord> zones,
        IReadOnlyList<San9ScenarioFixedBuildingRecord> buildings, IReadOnlyList<San9ScenarioTacticRecord> tactics,
        IReadOnlyDictionary<int, ImmutableArray<int>> enabledTactics)
    {
        if (squares.Count != San9ScenarioSquareArchive.RecordCount ||
            !squares.Select(s => s.Id).SequenceEqual(Enumerable.Range(0, squares.Count)) ||
            !terrains.Select(t => t.Id).SequenceEqual(Enumerable.Range(0, San9ScenarioTerrainArchive.RecordCount)) ||
            !formations.Select(f => f.Id).SequenceEqual(Enumerable.Range(0, San9ScenarioFormationArchive.RecordCount)) ||
            !tactics.Select(t => t.Id).SequenceEqual(Enumerable.Range(0, San9ScenarioTacticArchive.RecordCount)))
            throw new InvalidDataException("Incomplete military source catalogs.");
        var facilities = ConstructionContentCompiler.Facilities(buildings);
        if (!enabledTactics.Keys.Order().SequenceEqual(settlements.Select(s => s.Id).Order()))
            throw new InvalidDataException("Incomplete settlement tactic identities.");
        var places = settlements.Select(s =>
        {
            Footprint shape;
            bool destroyed = false;
            bool demolishable = false;
            var recovery = buildings.Single(b => b.Id == (s.Kind == SettlementKind.City ? 0 :
                zones.Single(z => z.Id + 50 == s.Id).FixedBuildingId)).RecoveryPerTurn;
            if (s.Kind == SettlementKind.City) shape = new(5, 5);
            else
            {
                var zone = zones.Single(z => z.Id + 50 == s.Id);
                var type = buildings.Single(b => b.Id == zone.FixedBuildingId);
                shape = new(type.FootprintWidth, type.FootprintHeight);
                destroyed = facilities.Single(f => f.Id == type.Id).DestroyedAtZeroDurability;
                demolishable = type.Id is >= 6 and <= 16;
            }
            // Ordinary coefficients use the adopted remaster scale; garrison range is two terrain cells.
            return new BattlefieldPlace(s.Id, s.Position, shape)
            {
                FacilityTypeId = s.Kind == SettlementKind.City ? 0 : zones.Single(z => z.Id + 50 == s.Id).FixedBuildingId,
                EnabledTacticIds = enabledTactics[s.Id],
                Combat = new(10, 10, 0, 10, 50), Attack = s.CanHostPersons ? new(10, false) { ElevationBonusPerLevel = 5, MaximumElevationBonus = 10 } : null, DestroyedAtZeroDurability = destroyed,
                WoundedRecovery = recovery, CanDemolish = demolishable
            };
        }).ToImmutableArray();
        var definition = new MilitaryDefinition(San9ScenarioSquareArchive.MapWidth, San9ScenarioSquareArchive.MapHeight,
            squares.Select(s => s.TerrainId ?? throw new InvalidDataException("Missing battlefield terrain.")).ToImmutableArray(),
            terrains.Select(t => Passage(t.Id)).ToImmutableArray(),
            formations.Select(f => new FormationRule(f.Id, checked(f.Mobility * 25), f.DeploymentCost,
                100000, 5, new(3, 3), Mode(f.Id))
                { Tactics = f.Id == 27 ? null : new(Mode(f.Id) == TravelMode.Water ? 10 : 15,
                        f.Id is 1 or 8 ? 50 : 0, f.Id == 2 ? 3 : 2,
                        Enumerable.Range(0, 4).Where(category => Favored(f.Id, category)).ToImmutableArray(),
                        Mode(f.Id) == TravelMode.Water ? tactics.Where(t => t.Id is >= 31 and <= 34 && t.SupportsArrayType(f.Id))
                            .Select(t => t.Id).ToImmutableArray() : [])
                        { ConfusionResistanceChance = f.Id == 7 ? 50 : 0, BypassesPassiveCounters = f.Id is 12 or 13 or 14 or 24 or 25 },
                    RequiredTacticId = tactics.Where(t => t.Id is >= 12 and <= 18 && t.SupportsArrayType(f.Id))
                        .Select(t => (int?)t.Id).SingleOrDefault(),
                    MartialTacticVulnerabilities = f.Id == 11 ? [0] : [],
                    Combat = new(f.AgainstUnits, f.AgainstSoldiers, f.AgainstWalls, f.DefensePower, f.Id == 7 ? 80 : 50),
                    Attack = f.Id == 27 ? null : new(f.Id == 10 ? 15 : f.Id is 21 or 26 ? 5 : 10, f.AgainstWalls > 0)
                    {
                        ElevationBonusPerLevel = Mode(f.Id) == TravelMode.Land ? 5 : 0,
                        MaximumElevationBonus = Mode(f.Id) == TravelMode.Land ? 10 : 0,
                        RequiresWaterOrHarbor = f.Id is 21 or 26
                    },
                    CaptureRanks = Enumerable.Range(0, 5).Select(i => f.Id is >= 20 and <= 26 ? BattleRank.Middle : f.GetDeploymentSlotPosition(i)?.Row switch
                    {
                        0 => BattleRank.Front, 1 => BattleRank.Middle, 2 => BattleRank.Rear,
                        _ => throw new InvalidDataException("Missing formation member position.")
                    }).ToImmutableArray() }).ToImmutableArray(), places)
        {
            Facilities = facilities,
            CityInfluence = new(8, 10),
            Rockfall = new(5, 25),
            Geography = new(MapCoordinates.UnitsPerCell,
                squares.Where(s => s.PathBlockingBits == 0x01fe).Select(s => new MapCell(s.GridX, s.GridY)).ToImmutableArray()),
            RegionIds = squares.Select(s => s.RegionCode).ToImmutableArray(),
            ConstructionSites = ConstructionContentCompiler.Sites(squares, buildings,
                settlements.Select(s => s.Id).Concat(zones.Where(z => z.XCoordinate is not null && z.YCoordinate is not null).Select(z => z.Id + 50)).ToHashSet()),
            Tactics = CompileTactics(tactics),
            Elevations = squares.Select(s => s.OffsetValue ?? -1).ToImmutableArray(),
            TrapTerrains = terrains.Select(t => TrapTerrain(t.Id)).ToImmutableArray(),
            HigherGroundTrap = new(38, 3),
            BlockedCells = squares.Where(s => s.IsPathBlocked).Select(s => new MapCell(s.GridX, s.GridY)).ToImmutableArray()
        };
        _ = new MilitaryRulebook(definition);
        return definition;
    }

    public static ImmutableArray<TacticRule> CompileTactics(IReadOnlyList<San9ScenarioTacticRecord> tactics)
    {
        if (!tactics.Select(t => t.Id).SequenceEqual(Enumerable.Range(0, San9ScenarioTacticArchive.RecordCount)))
            throw new InvalidDataException("Incomplete tactic source catalog.");
        return tactics.Select(t => new TacticRule(t.Id, t.SystemCode <= 8 ? t.SystemCode : null, t.Id switch
        {
            >= 0 and <= 11 or >= 31 and <= 34 => TacticEffect.Martial,
            >= 12 and <= 18 => TacticEffect.FormationTechnology,
            19 => TacticEffect.Construction, 20 => TacticEffect.StoneFormation,
            21 => TacticEffect.CounterTrap, 22 => TacticEffect.CounterConfusion,
            23 => TacticEffect.Confusion, 24 => TacticEffect.TrapSelection,
            25 => TacticEffect.Defection, 26 => TacticEffect.Illusion, 27 => TacticEffect.Taunt,
            28 => TacticEffect.Inspire, 29 => TacticEffect.Heal, 30 => TacticEffect.Sorcery,
            >= 35 and <= 38 => TacticEffect.Trap,
            39 or 40 => TacticEffect.FacilityAttack,
            _ => throw new InvalidDataException("Unknown TC101 tactic effect.")
        }, t.Power, t.MinimumInterval, t.TrainingRequirement, t.Id is <= 11 or >= 21 and <= 30,
            t.AffectsFacilities, Enumerable.Range(0, San9ScenarioFormationArchive.RecordCount)
                .Where(t.SupportsArrayType).ToImmutableArray())
            { EffectRadius = t.Id is 26 or 28 or 29 or 30 ? 3 : 0,
                FacilityAttack = t.Id switch { 39 => new(true, 0), 40 => new(false, 5), _ => null } }).ToImmutableArray();
    }

    private static TrapTerrainRule TrapTerrain(int id) => new(id, id switch
    {
        0 => 36,
        2 or 3 or 4 or 5 or 7 or 8 => 35,
        10 or 15 or 16 => 37,
        1 or 6 or 9 or 11 or 12 or 13 or 14 => (int?)null,
        _ => throw new InvalidDataException("Unknown TC101 trap terrain mapping.")
    }, id is 1 or 13 or 14);

    private static TravelMode Mode(int sourceId) => sourceId switch
    {
        >= 0 and <= 19 or 27 => TravelMode.Land,
        >= 20 and <= 26 => TravelMode.Water,
        _ => throw new InvalidDataException("Unknown TC101 formation movement mapping.")
    };

    private static bool Favored(int formation, int category) => category switch
    {
        0 => formation is 0 or 1 or 15 or 18 or 19,
        1 => formation is 2 or 3 or 16 or 17,
        2 => formation is 2 or 5 or 16,
        3 => formation is 4 or 5 or 8,
        _ => false
    };

    private static TerrainPassage Passage(int sourceId)
    {
        TerrainPassage result = sourceId switch
        {
            0 or 1 or 2 or 3 or 4 or 5 or 11 or 13 or 14 => new(sourceId, 10, null),
            6 or 7 or 8 => new(sourceId, 12, null),
            9 or 12 => new(sourceId, 12, 12),
            10 or 15 or 16 => new(sourceId, null, 10),
            _ => throw new InvalidDataException("Unknown TC101 terrain movement mapping.")
        };
        return result with { IsWater = sourceId is 10 or 15 or 16, StopsRockfall = sourceId == 6 };
    }
}
