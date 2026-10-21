using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Core.Domain;

public sealed record SoldierFootprint(uint MinimumSoldiers, Footprint Footprint);

public sealed record FormationRule(int Id, int MovementBudget, int DeploymentCost, uint SoldierCapacity,
    int OfficerCapacity, Footprint Footprint, TravelMode TravelMode)
{
    [JsonRequired] public ImmutableArray<SoldierFootprint> SoldierFootprints { get; init; } = [];
    public Footprint FootprintFor(uint soldiers)
    {
        var shape = Footprint;
        foreach (var band in SoldierFootprints)
        {
            if (soldiers < band.MinimumSoldiers) break;
            shape = band.Footprint;
        }
        return shape;
    }

    [JsonRequired] public bool IsCommon { get; init; } = true;
    public bool IsCandidateFor(ForceState force) => IsCommon || force.AdditionalFormationIds.Contains(Id);

    [JsonRequired] public FormationTactics? Tactics { get; init; }
    [JsonRequired] public int ConfusionResistanceChance { get; init; }
    [JsonRequired] public bool BypassesPassiveCounters { get; init; }
    [JsonRequired] public ImmutableArray<int> MartialTacticVulnerabilities { get; init; } = [];
    [JsonRequired] public int? RequiredTacticId { get; init; }
    [JsonRequired] public int? AutomaticTacticLearningId { get; init; }
    [JsonRequired] public int? CaptureTacticLearningId { get; init; }
    [JsonRequired] public CombatProfile? Combat { get; init; }
    [JsonRequired] public OrdinaryAttack? Attack { get; init; }
    [JsonRequired] public ImmutableArray<BattleRank> CaptureRanks { get; init; } = [];
    [JsonRequired] public bool DoublesCapture { get; init; }
    [JsonRequired] public bool PreventsTacticRescue { get; init; }
    [JsonRequired] public int SnipingChanceBonus { get; init; }
    [JsonRequired] public bool MeleeDeathUsesRanks { get; init; } = true;
    [JsonRequired] public bool DoublesDuelChance { get; init; }
    [JsonRequired] public bool GainsDailyCombatProficiency { get; init; } = true;
    [JsonRequired] public bool HalvesHostileStrategyChance { get; init; }
    [JsonRequired] public bool ImmuneToDisorder { get; init; }
}

public sealed record OrdinaryAttack(int Range, bool DamagesWalls)
{
    [JsonRequired] public int ElevationBonusPerLevel { get; init; }
    [JsonRequired] public int MaximumElevationBonus { get; init; }
    [JsonRequired] public bool RequiresWaterContact { get; init; }
    [JsonRequired] public bool RangedSiege { get; init; }
}

public sealed record FormationTactics(int OpportunityChance, int LinkMultiplier,
    ImmutableArray<int> FavoredCategories, ImmutableArray<int> AutomaticTactics);

public sealed record CombatProfile(int AgainstUnits, int AgainstGarrison, int AgainstWalls, int Defense, int WoundedPercent)
{
    internal void Validate()
    {
        if (AgainstUnits < 0 || AgainstGarrison < 0 || AgainstWalls < 0 || Defense < 0 || WoundedPercent is < 0 or > 100)
            throw new ArgumentException("Invalid combat profile.");
    }
}
public sealed record BattlefieldPlace(int Id, WorldPosition Position, Footprint Footprint)
{
    [JsonRequired] public ImmutableArray<int> EnabledTacticIds { get; init; } = [];
    [JsonRequired] public int? FacilityTypeId { get; init; }
    [JsonRequired] public bool CanDemolish { get; init; }
    [JsonRequired] public int? WoundedRecovery { get; init; }
    [JsonRequired] public CombatProfile? Combat { get; init; }
    [JsonRequired] public OrdinaryAttack? Attack { get; init; }
    [JsonRequired] public bool DestroyedAtZeroDurability { get; init; }
}
public sealed record TrapTerrainRule(int TerrainId, int? BaseTacticId, bool ProvidesHigherGround);
public sealed record HigherGroundTrapRule(int TacticId, int RadiusCells);

public sealed record MilitaryDefinition(int Width, int Height, ImmutableArray<int> TerrainIds,
    ImmutableArray<TerrainPassage> Terrains, ImmutableArray<FormationRule> Formations,
    ImmutableArray<BattlefieldPlace> Places)
{
    [JsonRequired] public FixedUnitFormations? ConstructionFormations { get; init; }
    [JsonRequired] public FixedUnitFormations? TransportFormations { get; init; }
    [JsonRequired] public int? BasicNavalFormationId { get; init; }
    [JsonRequired] public int? DefaultDeploymentFormationId { get; init; }
    [JsonRequired] public int? UnformedFormationId { get; init; }
    [JsonRequired] public int DefensiveTacticChance { get; init; }
    [JsonRequired] public bool BattleDeathsAllowed { get; init; } = true;
    [JsonRequired] public bool DuelCapturesAllowed { get; init; } = true;
    [JsonRequired] public int PolicyRadius { get; init; } = 50;
    [JsonRequired] public ImmutableArray<FacilityRule> Facilities { get; init; } = [];
    [JsonRequired] public ImmutableArray<ConstructionSite> ConstructionSites { get; init; } = [];
    [JsonRequired] public ImmutableArray<int> Elevations { get; init; } = [];
    [JsonRequired] public ImmutableArray<TrapTerrainRule> TrapTerrains { get; init; } = [];
    [JsonRequired] public HigherGroundTrapRule? HigherGroundTrap { get; init; }
    [JsonRequired] public ImmutableArray<int?> RegionIds { get; init; } = [];
    [JsonRequired] public ImmutableArray<TerritoryExits> TerritoryExits { get; init; } = [];
    [JsonRequired] public ImmutableArray<TacticRule> Tactics { get; init; } = [];
    [JsonRequired] public TacticLinkRules LinkRules { get; init; } = TacticLinkRules.Empty;
    [JsonRequired] public CityInfluenceRule? CityInfluence { get; init; }
    [JsonRequired] public BattlefieldGeography Geography { get; init; } = BattlefieldGeography.Empty;
    [JsonRequired] public RockfallRule? Rockfall { get; init; }
    [JsonRequired] public ImmutableArray<MapCell> BlockedCells { get; init; } = [];
}

public sealed partial class MilitaryRulebook
{
    public const uint WoundedCapacity = 50000;
    public int DefensiveTacticChance { get; }
    public bool BattleDeathsAllowed { get; }
    public bool DuelCapturesAllowed { get; }
    public MilitaryRulebook(MilitaryDefinition definition)
    {
        if (definition.DefensiveTacticChance is < 0 or > 100)
            throw new ArgumentException("Invalid defensive tactic chance.");
        DefensiveTacticChance = definition.DefensiveTacticChance;
        BattleDeathsAllowed = definition.BattleDeathsAllowed;
        DuelCapturesAllowed = definition.DuelCapturesAllowed;
        ArgumentOutOfRangeException.ThrowIfNegative(definition.PolicyRadius);
        PolicyRadius = definition.PolicyRadius;
        LinkRules = definition.LinkRules ?? throw new ArgumentException("Missing tactic link rules.");
        LinkRules.Validate();
        if (definition.TerrainIds.IsDefault || definition.Terrains.IsDefault || definition.Formations.IsDefault || definition.Places.IsDefault)
            throw new ArgumentException("Missing military definitions.");
        foreach (var formation in definition.Formations)
        {
            if (formation.SoldierFootprints.IsDefault || formation.SoldierFootprints.Any(b => !b.Footprint.IsValid) ||
                !formation.SoldierFootprints.Select(b => b.MinimumSoldiers).SequenceEqual(
                    formation.SoldierFootprints.Select(b => b.MinimumSoldiers).Distinct().Order()))
                throw new ArgumentException("Invalid soldier footprint bands.");
        }
        if (definition.BlockedCells.IsDefault) throw new ArgumentException("Missing battlefield blocking cells.");
        Map = new(definition.Width, definition.Height, definition.TerrainIds, definition.Terrains, definition.BlockedCells);
        CityInfluence = definition.CityInfluence;
        Geography = definition.Geography ?? throw new ArgumentException("Missing battlefield geography.");
        Rockfall = definition.Rockfall;
        if (CityInfluence is { } influence && (influence.RadiusCells < 0 || influence.LandTacticBonus is < 0 or > 100) ||
            Geography.FootprintScale <= 0 || Geography.BlockedCells.IsDefault ||
            Geography.BlockedCells.Distinct().Count() != Geography.BlockedCells.Length || Geography.BlockedCells.Any(c =>
                c.X < 0 || c.Y < 0 || c.X >= Map.Width || c.Y >= Map.Height))
            throw new ArgumentException("Invalid battlefield geography.");
        if (Rockfall is { } rocks && (rocks.PropagationSteps < 0 || rocks.AttackRange <= 0 || definition.Elevations.Length != definition.TerrainIds.Length))
            throw new ArgumentException("Invalid rockfall geography.");
        geographicBlocked = Geography.BlockedCells.ToImmutableHashSet();
        RegionIds = definition.RegionIds;
        if (RegionIds.IsDefault || !RegionIds.IsEmpty && RegionIds.Length != definition.TerrainIds.Length || RegionIds.Any(id => id < 0))
            throw new ArgumentException("Invalid battlefield region geography.");
        territoryBorders = BuildTerritoryBorders(definition.TerritoryExits);
        Formations = definition.Formations.ToImmutableDictionary(f => f.Id);
        if (Formations.Values.Any(f => f.SnipingChanceBonus is < 0 or > 100))
            throw new ArgumentException("Invalid sniping formation bonus.");
        BasicNavalFormationId = definition.BasicNavalFormationId;
        DefaultDeploymentFormationId = definition.DefaultDeploymentFormationId;
        if (DefaultDeploymentFormationId is int preferred &&
            (!Formations.TryGetValue(preferred, out var defaultLand) || defaultLand.TravelMode != TravelMode.Land))
            throw new ArgumentException("Invalid default deployment formation.");
        ConstructionFormations = definition.ConstructionFormations;
        TransportFormations = definition.TransportFormations;
        if (BasicNavalFormationId is int naval &&
            (!Formations.TryGetValue(naval, out var basicShip) || basicShip.TravelMode != TravelMode.Water))
            throw new ArgumentException("Invalid basic naval formation reference.");
        UnformedFormationId = definition.UnformedFormationId;
        if (UnformedFormationId is int unformed && (!Formations.TryGetValue(unformed, out var profile) || profile.Combat is null))
            throw new ArgumentException("Invalid unformed combat formation reference.");
        if (definition.Tactics.IsDefault) throw new ArgumentException("Missing tactic definitions.");
        Tactics = definition.Tactics.ToImmutableDictionary(t => t.Id);
        foreach (var tactic in Tactics.Values) tactic.Validate(Formations);
        if (definition.Facilities.IsDefault || definition.ConstructionSites.IsDefault)
            throw new ArgumentException("Missing construction definitions.");
        Facilities = definition.Facilities.ToImmutableDictionary(f => f.Id);
        foreach (var facility in Facilities.Values) facility.Validate(Tactics);
        ConstructionSites = definition.ConstructionSites.ToImmutableDictionary(s => (s.RegionId, s.Position));
        foreach (var site in ConstructionSites.Values)
        {
            if (site.RegionId < 0 || site.FacilityIds.IsDefaultOrEmpty || site.FacilityIds.Distinct().Count() != site.FacilityIds.Length ||
                site.FacilityIds.Any(id => !Facilities.TryGetValue(id, out var facility) || !facility.Buildable ||
                    FacilityArea(site.Position, facility.Id) is var area &&
                        (area.Left < 0 || area.Top < 0 || area.Right > Map.FineWidth || area.Bottom > Map.FineHeight)))
                throw new ArgumentException("Invalid construction site.");
        }
        if (definition.Elevations.IsDefault || definition.TrapTerrains.IsDefault)
            throw new ArgumentException("Missing trap geography.");
        bool hasSelection = Tactics.Values.Any(t => t.Effect == TacticEffect.TrapSelection);
        bool hasGeography = !definition.TrapTerrains.IsEmpty || definition.HigherGroundTrap is not null;
        bool ValidTrap(int? id) => id is null || Tactics.TryGetValue(id.Value, out var tactic) && tactic.Effect == TacticEffect.Trap;
        if ((!definition.Elevations.IsEmpty || hasSelection || hasGeography) &&
                definition.Elevations.Length != definition.TerrainIds.Length ||
            (hasSelection || hasGeography) &&
                (!definition.TrapTerrains.Select(t => t.TerrainId).Order().SequenceEqual(definition.Terrains.Select(t => t.Id).Order()) ||
                 definition.TrapTerrains.Any(t => !ValidTrap(t.BaseTacticId))) ||
            definition.HigherGroundTrap is { } higher && (higher.RadiusCells < 0 || !ValidTrap(higher.TacticId)))
            throw new ArgumentException("Invalid trap geography or tactic reference.");
        terrainIds = definition.TerrainIds;
        elevations = definition.Elevations;
        trapTerrains = definition.TrapTerrains.ToImmutableDictionary(t => t.TerrainId);
        higherGroundTrap = definition.HigherGroundTrap;
        foreach (var formation in Formations.Values)
            if (formation.Tactics is { } tactics && (tactics.OpportunityChance is < 0 or > 100 ||
                tactics.LinkMultiplier <= 0 || tactics.FavoredCategories.IsDefault || tactics.AutomaticTactics.IsDefault ||
                tactics.FavoredCategories.Any(c => c is < 0 or > 8) ||
                tactics.FavoredCategories.Distinct().Count() != tactics.FavoredCategories.Length ||
                tactics.AutomaticTactics.Distinct().Count() != tactics.AutomaticTactics.Length ||
                tactics.AutomaticTactics.Any(id => !Tactics.TryGetValue(id, out var rule) || rule.Effect != TacticEffect.Martial ||
                    !rule.CompatibleFormations.Contains(formation.Id))))
                throw new ArgumentException("Invalid formation tactic profile.");
        if (Formations.Values.Any(f => f.RequiredTacticId is int id &&
            (!Tactics.TryGetValue(id, out var tactic) || tactic.Effect != TacticEffect.FormationTechnology)))
            throw new ArgumentException("Unknown formation technology reference.");
        foreach (var formation in Formations.Values)
        {
            formation.Combat?.Validate();
            if (formation.ConfusionResistanceChance is < 0 or > 100)
                throw new ArgumentException("Invalid formation confusion resistance.");
        }
        if (Formations.Values.Any(f => f.MartialTacticVulnerabilities.IsDefault ||
            f.MartialTacticVulnerabilities.Any(c => c is < 0 or > 8) ||
            f.MartialTacticVulnerabilities.Distinct().Count() != f.MartialTacticVulnerabilities.Length))
            throw new ArgumentException("Invalid formation tactic vulnerabilities.");
        if (Formations.Values.Any(f => f.CaptureRanks.IsDefault || f.CaptureRanks.Any(r => !Enum.IsDefined(r)) ||
            (!f.CaptureRanks.IsEmpty || f.Attack is not null) && f.CaptureRanks.Length != f.OfficerCapacity))
            throw new ArgumentException("Missing formation capture ranks.");
        Places = definition.Places.ToImmutableDictionary(p => p.Id);
        if (Places.Values.Select(p => p.Footprint).Concat(Facilities.Values.Select(f => f.Footprint))
            .Any(shape => (long)shape.Width * Geography.FootprintScale > int.MaxValue || (long)shape.Height * Geography.FootprintScale > int.MaxValue))
            throw new ArgumentException("City influence footprint exceeds coordinate capacity.");
        if (Places.Values.Any(p => p.EnabledTacticIds.IsDefault || p.EnabledTacticIds.Distinct().Count() != p.EnabledTacticIds.Length ||
            p.EnabledTacticIds.Any(id => !Tactics.ContainsKey(id)))) throw new ArgumentException("Invalid enabled settlement tactics.");
        if (Places.Values.Any(p => p.FacilityTypeId is int id && !Facilities.ContainsKey(id) ||
            !Facilities.IsEmpty && p.FacilityTypeId is null)) throw new ArgumentException("Unknown facility type reference.");
        foreach (var place in Places.Values) place.Combat?.Validate();
        var attacks = Formations.Values.Select(f => f.Attack).Concat(Places.Values.Select(p => p.Attack))
            .Concat(Facilities.Values.Select(f => f.Attack)).OfType<OrdinaryAttack>().ToArray();
        if (attacks.Any(a => a.ElevationBonusPerLevel < 0 || a.MaximumElevationBonus < 0 ||
            (a.ElevationBonusPerLevel == 0) != (a.MaximumElevationBonus == 0) ||
            (long)a.Range + a.MaximumElevationBonus > int.MaxValue) ||
            attacks.Any(a => a.ElevationBonusPerLevel > 0) && elevations.Length != terrainIds.Length)
            throw new ArgumentException("Invalid attack elevation rules.");
        if (Places.Values.Any(p => p.WoundedRecovery is < 0)) throw new ArgumentException("Invalid wounded recovery definition.");
        if (Formations.Values.Any(f => f.AutomaticTacticLearningId is int id &&
            (!Tactics.TryGetValue(id, out var tactic) || tactic.Category is null)))
            throw new ArgumentException("Invalid automatic tactic learning reference.");
        if (Formations.Values.Any(f => f.CaptureTacticLearningId is int id &&
            (!Tactics.TryGetValue(id, out var tactic) || tactic.Category is null)))
            throw new ArgumentException("Invalid capture tactic learning reference.");
        if (Formations.Values.Any(f => f.Attack is { Range: <= 0 } || f.Attack is not null && f.Combat is null) ||
            Places.Values.Any(p => p.Attack is { Range: <= 0 } || p.Attack is not null && p.Combat is null))
            throw new ArgumentException("Invalid ordinary attack definition.");
        if (Formations.Values.Any(f => f.Id < 0 || f.RequiredTacticId < 0 || f.MovementBudget <= 0 || (long)f.MovementBudget + Map.MaximumStepCost(f.TravelMode) - 1 > int.MaxValue || f.DeploymentCost < 0 || f.SoldierCapacity == 0 ||
                f.OfficerCapacity <= 0 || !f.Footprint.IsValid || !Enum.IsDefined(f.TravelMode)) ||
            Places.Values.Any(p => p.Id < 0 || !p.Footprint.IsValid || BuildingArea(p) is var area &&
                (area.Left < 0 || area.Top < 0 || area.Right > Map.FineWidth || area.Bottom > Map.FineHeight)))
            throw new ArgumentException("Invalid military formation or place.");
        ValidateFixedFormations();
    }

    public int? BasicNavalFormationId { get; }
    public int? DefaultDeploymentFormationId { get; }
    public BattlefieldMap Map { get; }
    public ImmutableArray<int?> RegionIds { get; }

    public int? RegionAt(WorldPosition position)
    {
        var cell = MapCoordinates.Cell(position);
        if (cell.X >= Map.Width || cell.Y >= Map.Height) throw new ArgumentOutOfRangeException(nameof(position));
        return RegionIds.IsEmpty ? null : RegionIds[cell.Y * Map.Width + cell.X];
    }
    private readonly ImmutableArray<int> terrainIds;
    private readonly ImmutableArray<int> elevations;
    private readonly ImmutableDictionary<int, TrapTerrainRule> trapTerrains;
    private readonly HigherGroundTrapRule? higherGroundTrap;

    public TacticRule? TrapAt(WorldPosition position)
    {
        var cell = MapCoordinates.Cell(position);
        if (cell.X >= Map.Width || cell.Y >= Map.Height) throw new ArgumentOutOfRangeException(nameof(position));
        if (trapTerrains.IsEmpty) return null;
        int index = cell.Y * Map.Width + cell.X;
        if (higherGroundTrap is { } higher)
        {
            // floor(sqrt(distanceSquared)) <= radius, without computing a square root.
            long limit = ((long)higher.RadiusCells + 1) * ((long)higher.RadiusCells + 1);
            int minX = (int)Math.Max(0L, (long)cell.X - higher.RadiusCells);
            int maxX = (int)Math.Min(Map.Width - 1L, (long)cell.X + higher.RadiusCells);
            int minY = (int)Math.Max(0L, (long)cell.Y - higher.RadiusCells);
            int maxY = (int)Math.Min(Map.Height - 1L, (long)cell.Y + higher.RadiusCells);
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                long dx = x - cell.X, dy = y - cell.Y;
                int neighbor = y * Map.Width + x;
                if (dx * dx + dy * dy < limit && elevations[neighbor] > elevations[index] &&
                    trapTerrains[terrainIds[neighbor]].ProvidesHigherGround)
                    return Tactics[higher.TacticId];
            }
        }
        return trapTerrains[terrainIds[index]].BaseTacticId is int id ? Tactics[id] : null;
    }
    public int? UnformedFormationId { get; }
    public CombatProfile? CombatFormation(UnitState unit) => Formations[
        unit.Condition is UnitCondition.NoFormation or UnitCondition.Confused or UnitCondition.Routed && UnformedFormationId is int unformed
            ? unformed : Formation(unit).Id].Combat;
    public ImmutableDictionary<int, FormationRule> Formations { get; }
    public TacticLinkRules LinkRules { get; }
    public CityInfluenceRule? CityInfluence { get; }
    public BattlefieldGeography Geography { get; }
    public RockfallRule? Rockfall { get; }
    private readonly ImmutableHashSet<MapCell> geographicBlocked;

    public bool HasCityInfluence(int ownerId, WorldPosition position, IEnumerable<SettlementState> settlements)
    {
        if (CityInfluence is not { } rule || position.X < 0 || position.Y < 0 ||
            position.X >= Map.FineWidth || position.Y >= Map.FineHeight) return false;
        var target = MapCoordinates.Cell(position);
        var buildings = settlements.Where(s => !s.IsDestroyed && s.Facility?.BuilderUnitId is null && TryPlace(s, out _))
            .Select(s => (State: s, Area: SettlementArea(s)))
            .ToArray();
        var seeds = new HashSet<MapCell>();
        foreach (var building in buildings.Where(b => b.State.Kind == SettlementKind.City && b.State.OwnerId == ownerId))
            seeds.UnionWith(GeographicCells(building.Area));
        if (seeds.Contains(target)) return true;
        bool Blocked(MapCell cell) => geographicBlocked.Contains(cell) || buildings.Any(b => Inside(cell, b.Area));
        if (seeds.Count == 0 || Blocked(target)) return false;
        // Reverse search counts the queried cell as the first outside step; entering a seed costs zero.
        var queue = new Queue<(MapCell Cell, int Distance)>();
        var visited = new HashSet<MapCell> { target };
        queue.Enqueue((target, 1));
        while (queue.TryDequeue(out var current))
        {
            if (current.Distance > rule.RadiusCells) continue;
            foreach (var next in new MapCell[] { new(current.Cell.X, current.Cell.Y - 1), new(current.Cell.X + 1, current.Cell.Y),
                new(current.Cell.X, current.Cell.Y + 1), new(current.Cell.X - 1, current.Cell.Y) })
            {
                if (seeds.Contains(next)) return true;
                if (next.X < 0 || next.Y < 0 || next.X >= Map.Width || next.Y >= Map.Height || !visited.Add(next) || Blocked(next)) continue;
                queue.Enqueue((next, current.Distance + 1));
            }
        }
        return false;
    }
    public ImmutableDictionary<int, TacticRule> Tactics { get; }
    public int PolicyRadius { get; }
    public ImmutableDictionary<int, BattlefieldPlace> Places { get; }
    public ImmutableDictionary<int, FacilityRule> Facilities { get; }
    public ImmutableDictionary<(int RegionId, WorldPosition Position), ConstructionSite> ConstructionSites { get; }

    public TacticRule? FacilityTactic(SettlementState settlement)
    {
        if (!TryPlace(settlement, out var place) || place.FacilityTypeId is not int type || Facilities[type].Tactics is not { } profile) return null;
        return profile.TacticPriority.Where(id => !profile.RequiresEnabledTactic || place.EnabledTacticIds.Contains(id))
            .Select(id => Tactics[id]).FirstOrDefault();
    }

    public FacilityAttackProfile FacilityAttack(SettlementState settlement)
    {
        var place = Place(settlement);
        if (settlement.IsDestroyed || settlement.Facility?.BuilderUnitId is not null || place.Attack is null)
            return new(null, false);
        var ids = place.EnabledTacticIds.Concat(place.FacilityTypeId is int type ? Facilities[type].TacticIds : []).Distinct();
        var modifiers = ids.Select(id => Tactics[id].FacilityAttack)
            .Append(place.FacilityTypeId is int fixedType ? Facilities[fixedType].InnateAttack : null).OfType<FacilityAttackModifier>().ToArray();
        int bonus = modifiers.Select(m => m.RangeBonus).DefaultIfEmpty(0).Max();
        return new(place.Attack with { Range = checked(place.Attack.Range + bonus) }, modifiers.Any(m => m.UsesRockfallPower));
    }

    public bool IsHarbor(SettlementState settlement) => !settlement.IsDestroyed && TryPlace(settlement, out var place) &&
        place.FacilityTypeId is int type && Facilities[type].IsHarbor;

    public int AttackRange(OrdinaryAttack? attack, MapOccupancy source, MapOccupancy target, bool targetIsSettlement,
        Func<WorldPosition, bool>? blocksContact = null)
    {
        if (!CanAttackTarget(attack, target, targetIsSettlement)) return 0;
        if (attack!.RequiresWaterContact && !HasWaterContact(source, target, attack.Range, targetIsSettlement, blocksContact)) return 0;
        if (attack.ElevationBonusPerLevel == 0) return attack.Range;
        var from = MapCoordinates.Cell(source.Position); var to = MapCoordinates.Cell(target.Position);
        long difference = (long)elevations[from.Y * Map.Width + from.X] - elevations[to.Y * Map.Width + to.X];
        return checked(attack.Range + (int)Math.Min(attack.MaximumElevationBonus, Math.Max(0L, difference) * attack.ElevationBonusPerLevel));
    }

    public BattlefieldPlace Place(SettlementState settlement)
    {
        if (settlement.Facility is not { } instance) return Places[settlement.Id];
        var type = Facilities[instance.TypeId];
        return new(settlement.Id, instance.Position, type.Footprint)
        {
            FacilityTypeId = type.Id, CanDemolish = type.Buildable, WoundedRecovery = type.WoundedRecovery, EnabledTacticIds = type.TacticIds,
            Combat = type.Combat, Attack = instance.BuilderUnitId is null ? type.Attack : null,
            DestroyedAtZeroDurability = type.DestroyedAtZeroDurability
        };
    }

    public bool TryPlace(SettlementState settlement, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out BattlefieldPlace? place)
    {
        if (settlement.Facility is null) return Places.TryGetValue(settlement.Id, out place);
        if (!Facilities.ContainsKey(settlement.Facility.TypeId)) { place = null; return false; }
        place = Place(settlement);
        return true;
    }
}

public enum MilitaryOrderKind { Move, Return, Transport, AttackUnit, AttackSettlement, Construct }
public sealed record ConstructionPlan(int RegionId, int FacilityTypeId, WorldPosition Position, int LeaderId);
public sealed record MilitaryOrder(MilitaryOrderKind Kind, WorldPosition? Position, int? SettlementId)
{
    [JsonRequired] public ConstructionPlan? Construction { get; init; }
    [JsonRequired] public int? UnitId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public bool IsAttack => Kind is MilitaryOrderKind.AttackUnit or MilitaryOrderKind.AttackSettlement;
    [System.Text.Json.Serialization.JsonIgnore] public bool IsValid => Enum.IsDefined(Kind) && (Kind == MilitaryOrderKind.Construct || Construction is null) && (Kind switch
    {
        MilitaryOrderKind.Construct => Position is null && SettlementId is null && UnitId is null &&
            Construction is { RegionId: >= 0, FacilityTypeId: >= 0, LeaderId: >= 0, Position.X: >= 0, Position.Y: >= 0 },
        MilitaryOrderKind.Move => Position is { X: >= 0, Y: >= 0 } && SettlementId is null && UnitId is null,
        MilitaryOrderKind.AttackUnit => Position is null && SettlementId is null && UnitId is >= 0,
        _ => Position is null && SettlementId is >= 0 && UnitId is null
    });
}
