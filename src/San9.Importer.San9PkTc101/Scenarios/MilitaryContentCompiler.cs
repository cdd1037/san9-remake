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
        var facilities = ConstructionContentCompiler.Facilities(buildings, formations);
        if (!enabledTactics.Keys.Order().SequenceEqual(settlements.Select(s => s.Id).Order()))
            throw new InvalidDataException("Incomplete settlement tactic identities.");
        var places = settlements.Select(s =>
        {
            var facility = facilities.Single(f => f.Id == (s.Kind == SettlementKind.City ? 0 : zones.Single(z => z.Id + 50 == s.Id).FixedBuildingId));
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
            return new BattlefieldPlace(s.Id, s.Position, shape)
            {
                FacilityTypeId = facility.Id,
                EnabledTacticIds = enabledTactics[s.Id],
                Combat = facility.Combat, Attack = s.CanHostPersons ? new(10, false) { IsRangedAttack = true, ElevationBonusPerLevel = 5, MaximumElevationBonus = 10 } : null, DestroyedAtZeroDurability = destroyed,
                WoundedRecovery = recovery, CanDemolish = demolishable
            };
        }).ToImmutableArray();
        var definition = new MilitaryDefinition(San9ScenarioSquareArchive.MapWidth, San9ScenarioSquareArchive.MapHeight,
            squares.Select(s => s.TerrainId ?? throw new InvalidDataException("Missing battlefield terrain.")).ToImmutableArray(),
            terrains.Select(t => Passage(t.Id)).ToImmutableArray(),
            formations.Select(f => new FormationRule(f.Id, checked(f.Mobility * 5), f.DeploymentCost,
                100000, 5, Shape(f.Id), Mode(f.Id))
                { SoldierFootprints = Mode(f.Id) == TravelMode.Land
                    ? new uint[] { 6000, 12000, 20000, 30000, 40000, 50000 }
                        .Select((minimum, index) => new SoldierFootprint(minimum, new(index + 5, index + 5))).ToImmutableArray() : [],
                    Vision = f.Id == 27 ? null : new(checked(f.VisionRadius * MapCoordinates.UnitsPerCell), f.VisionAngleDegrees),
                    IsCommon = f.Id is >= 0 and <= 11 or >= 20 and <= 23,
                    SupportsSpearDefense = f.Id == 7,
                    SupportsShieldCover = f.Id == 7,
                    SupportsSpearBowSynergy = f.Id == 4,
                    Tactics = f.Id is 13 or 14 or 24 or 25 or 27 ? null : new(15,
                        f.Id == 2 ? 3 : 2,
                        Enumerable.Range(0, 4).Where(category => Favored(f.Id, category)).ToImmutableArray(),
                        f.Id is >= 20 and <= 23 ? tactics.Where(t => t.Id is >= 31 and <= 34 && t.SupportsArrayType(f.Id))
                            .Select(t => t.Id).ToImmutableArray() : [])
                        { AutomaticOpportunityChance = Mode(f.Id) == TravelMode.Water ? 10 : null },
                    ConfusionResistanceChance = f.Id == 7 ? 50 : 0,
                    BypassesPassiveCounters = f.Id is 12 or 13 or 14 or 24 or 25,
                    RequiredTacticId = tactics.Where(t => t.Id is >= 12 and <= 18 && t.SupportsArrayType(f.Id))
                        .Select(t => (int?)t.Id).SingleOrDefault(),
                    AutomaticTacticLearningId = f.Id is >= 21 and <= 23 ? f.Id - 9 : null,
                    CaptureTacticLearningId = f.Id is >= 8 and <= 11 ? f.Id + 7 : null,
                    MartialTacticVulnerabilities = f.Id == 11 ? [0] : [],
                    Combat = new(f.AgainstUnits, f.AgainstSoldiers, f.AgainstWalls, f.DefensePower, f.Id == 7 ? 80 : 50),
                    Attack = f.Id == 27 ? null : new(f.Id == 10 ? 15 : f.Id is 21 or 26 ? 5 : 10, f.AgainstWalls > 0)
                    {
                        ElevationBonusPerLevel = Mode(f.Id) == TravelMode.Land ? 5 : 0,
                        MaximumElevationBonus = Mode(f.Id) == TravelMode.Land ? 10 : 0,
                        RequiresWaterContact = f.Id is 21 or 26, RangedSiege = f.Id == 10
                    },
                    DoublesCapture = f.Id == 1,
                    PreventsTacticRescue = f.Id == 1,
                    SnipingChanceBonus = f.Id == 5 ? 30 : 0,
                    MeleeDeathUsesRanks = f.Id is not (12 or 13 or 14 or 24 or 25 or 27),
                    DoublesDuelChance = f.Id == 3,
                    GainsDailyCombatProficiency = f.Id is not (13 or 14),
                    HalvesHostileStrategyChance = f.Id == 7,
                    ImmuneToDisorder = f.Id == 27,
                    Positions = Enumerable.Range(0, 5).Select(i => f.Id is >= 20 and <= 26
                        ? new FormationPosition(i, BattleRank.Middle)
                        : f.GetDeploymentSlotPosition(i) is { } position
                            ? new FormationPosition(position.Column, (BattleRank)position.Row)
                            : throw new InvalidDataException("Missing formation member position.")).ToImmutableArray() }).ToImmutableArray(), places)
        {
            TroopTypes = MixedTroopPreset.TroopTypes([0, 1, 2], [3, 4, 5], [6, 7, 8], [9, 10, 11]),
            Armaments = ArmamentPreset.Basic(),
            NavalTraining = MixedTroopPreset.NavalTraining(31, 32, 33, 34),
            SiegeEquipment = MixedTroopPreset.SiegeEquipment(8, 9, 10, 11),
            MixedTroopCombat = MixedTroopPreset.Combat(),
            ResearchProjects = MixedTroopPreset.TroopResearchProjects(),
            BasicNavalFormationId = BasicNavalFormationId,
            CompanionCombat = new(new(2, 5), new(5, 4),
                [new(21, [12, 14]), new(22, [13, 14]), new(23, [14])], new(5, [8, 9, 10, 11]), [8], 28),
            CompanionTactics = new([new(1, new(10, 3, false, [10])), new(6, new(10, 3, false, [9])),
                new(9, new(10, 3, false, [10]))], new(21, 33, 31, 5), new(23, 32)),
            DefaultDeploymentFormationId = 6,
            ConstructionFormations = new(13, 24),
            TransportFormations = new(14, 25),
            UnformedFormationId = 27,
            Facilities = facilities,
            CityInfluence = new(8, 10),
            Rockfall = new(5, 25),
            Geography = new(MapCoordinates.UnitsPerCell,
                squares.Where(s => s.PathBlockingBits == 0x01fe).Select(s => new MapCell(s.GridX, s.GridY)).ToImmutableArray()),
            RegionIds = squares.Select(s => s.RegionCode).ToImmutableArray(),
            TerritoryExits = squares.Select(s => PersonnelGeographyCompiler.TerritoryExits(s.SquareFlags)).ToImmutableArray(),
            ConstructionSites = ConstructionContentCompiler.Sites(squares, buildings,
                settlements.Select(s => s.Id).Concat(zones.Where(z => z.XCoordinate is not null && z.YCoordinate is not null).Select(z => z.Id + 50)).ToHashSet()),
            DefensiveTacticChance = 50,
            Tactics = CompileTactics(tactics),
            Elevations = squares.Select(s => s.OffsetValue ?? -1).ToImmutableArray(),
            TrapTerrains = terrains.Select(t => TrapTerrain(t.Id)).ToImmutableArray(),
            HigherGroundTrap = new(38, 3),
            PolicyRadius = 10 * MapCoordinates.UnitsPerCell,
            VisionSizeBands = [new(1, 4), new(6000, 5), new(12000, 6), new(20000, 7),
                new(30000, 8), new(40000, 9), new(50000, 10)],
            BlockedCells = squares.Where(s => s.IsPathBlocked).Select(s => new MapCell(s.GridX, s.GridY)).ToImmutableArray()
        };
        definition = AddMixedTroopFormations(definition);
        _ = new MilitaryRulebook(definition);
        return definition;
    }

    public static MilitaryDefinition AddMixedTroopFormations(MilitaryDefinition definition)
    {
        var addedFormations = new[]
        {
            (Id: 28, BaseId: 7, Research: "formation-yanyue"),
            (Id: 29, BaseId: 4, Research: "formation-henge"),
            (Id: 30, BaseId: 1, Research: "formation-gouxing"),
            (Id: 31, BaseId: 2, Research: "formation-chexuan")
        };
        definition = definition with
        {
            Formations = definition.Formations.Select(formation => formation with
            {
                ResearchProjectId = formation.Id switch
                {
                    8 => "siege-tower", 9 => "siege-ram", 10 => "siege-stone", 11 => "siege-elephant",
                    21 => "naval-mengchong", 22 => "naval-louchuan", 23 => "naval-doujian", _ => null
                }
            }).ToImmutableArray().AddRange(addedFormations.Select(item =>
                definition.Formations.Single(f => f.Id == item.BaseId) with
                {
                    Id = item.Id, MixedTroopsOnly = true, ResearchProjectId = item.Research,
                    Positions = MixedTroopPreset.FormationPositions(item.Id - 28),
                    SupportsSpearDefense = item.Id == 28,
                    SupportsShieldCover = item.Id == 28,
                    SupportsSpearBowSynergy = item.Id is 28 or 29
                })),
            Tactics = definition.Tactics.Select(tactic => tactic with
            {
                CompatibleFormations = tactic.CompatibleFormations.AddRange(addedFormations
                    .Where(item => tactic.CompatibleFormations.Contains(item.BaseId)).Select(item => item.Id))
            }).ToImmutableArray()
        };
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
            { IsRangedAttack = t.Id is >= 6 and <= 11 or 31 or 32,
                EffectRadius = t.Id is 26 or 29 or 30 ? 3 : 0, ContactRange = t.Id is >= 0 and <= 5 ? 5 : null,
                CanRescuePrisoners = t.Id is >= 0 and <= 5 or 33 or 34,
                SnipingProficiencyCategory = t.Id is >= 6 and <= 11 or 31 or 32 ? 3 : null,
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

    private static Footprint Shape(int id)
    {
        int side = id switch { 21 or 26 => 6, 22 => 8, 23 => 10, _ => 4 };
        return new(side, side);
    }

    internal const int BasicNavalFormationId = 20;

    internal static TravelMode Mode(int sourceId) => sourceId switch
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
