using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed record FormationRule(int Id, int MovementBudget, int DeploymentCost, uint SoldierCapacity,
    int OfficerCapacity, Footprint Footprint, TravelMode TravelMode)
{
    [System.Text.Json.Serialization.JsonRequired] public FormationTactics? Tactics { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int? RequiredTacticId { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public CombatProfile? Combat { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public OrdinaryAttack? Attack { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public ImmutableArray<BattleRank> CaptureRanks { get; init; } = [];
}

public sealed record OrdinaryAttack(int Range, bool DamagesWalls);

public sealed record FormationTactics(int OpportunityChance, int CounterChance, int LinkMultiplier,
    ImmutableArray<int> FavoredCategories, ImmutableArray<int> AutomaticTactics)
{
    [System.Text.Json.Serialization.JsonRequired] public int ConfusionResistanceChance { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool BypassesPassiveCounters { get; init; }
}

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
    [System.Text.Json.Serialization.JsonRequired] public int? FacilityTypeId { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool CanDemolish { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int? WoundedRecovery { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public CombatProfile? Combat { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public OrdinaryAttack? Attack { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool DestroyedAtZeroDurability { get; init; }

    public IEnumerable<WorldPosition> Entrances(Footprint footprint)
    {
        var area = new MapOccupancy(Position, Footprint);
        for (int x = (int)area.Left - (footprint.Width - 1) / 2; x < area.Right + footprint.Width / 2; x++)
            yield return new(x, (int)area.Top - (footprint.Height + 1) / 2);
        for (int y = (int)area.Top - (footprint.Height - 1) / 2; y < area.Bottom + footprint.Height / 2; y++)
            yield return new((int)area.Right + footprint.Width / 2, y);
        for (int x = (int)area.Left - (footprint.Width - 1) / 2; x < area.Right + footprint.Width / 2; x++)
            yield return new(x, (int)area.Bottom + footprint.Height / 2);
        for (int y = (int)area.Top - (footprint.Height - 1) / 2; y < area.Bottom + footprint.Height / 2; y++)
            yield return new((int)area.Left - (footprint.Width + 1) / 2, y);
    }
}
public sealed record TrapTerrainRule(int TerrainId, int? BaseTacticId, bool ProvidesHigherGround);
public sealed record HigherGroundTrapRule(int TacticId, int RadiusCells);

public sealed record MilitaryDefinition(int Width, int Height, ImmutableArray<int> TerrainIds,
    ImmutableArray<TerrainPassage> Terrains, ImmutableArray<FormationRule> Formations,
    ImmutableArray<BattlefieldPlace> Places)
{
    [System.Text.Json.Serialization.JsonRequired] public ImmutableArray<FacilityRule> Facilities { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired] public ImmutableArray<ConstructionSite> ConstructionSites { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired] public ImmutableArray<int> Elevations { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired] public ImmutableArray<TrapTerrainRule> TrapTerrains { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired] public HigherGroundTrapRule? HigherGroundTrap { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public ImmutableArray<int?> RegionIds { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired] public ImmutableArray<TacticRule> Tactics { get; init; } = [];
    [System.Text.Json.Serialization.JsonRequired] public ImmutableArray<MapCell> BlockedCells { get; init; } = [];
}

public sealed class MilitaryRulebook
{
    public const uint WoundedCapacity = 50000;
    public MilitaryRulebook(MilitaryDefinition definition)
    {
        if (definition.TerrainIds.IsDefault || definition.Terrains.IsDefault || definition.Formations.IsDefault || definition.Places.IsDefault)
            throw new ArgumentException("Missing military definitions.");
        if (definition.BlockedCells.IsDefault) throw new ArgumentException("Missing battlefield blocking cells.");
        Map = new(definition.Width, definition.Height, definition.TerrainIds, definition.Terrains, definition.BlockedCells);
        RegionIds = definition.RegionIds;
        if (RegionIds.IsDefault || !RegionIds.IsEmpty && RegionIds.Length != definition.TerrainIds.Length || RegionIds.Any(id => id < 0))
            throw new ArgumentException("Invalid battlefield region geography.");
        Formations = definition.Formations.ToImmutableDictionary(f => f.Id);
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
                    new MapOccupancy(site.Position, facility.Footprint) is var area &&
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
            if (formation.Tactics is { } tactics && (tactics.OpportunityChance is < 0 or > 100 || tactics.CounterChance is < 0 or > 100 ||
                tactics.ConfusionResistanceChance is < 0 or > 100 || tactics.LinkMultiplier <= 0 || tactics.FavoredCategories.IsDefault || tactics.AutomaticTactics.IsDefault ||
                tactics.FavoredCategories.Any(c => c is < 0 or > 8) ||
                tactics.FavoredCategories.Distinct().Count() != tactics.FavoredCategories.Length ||
                tactics.AutomaticTactics.Distinct().Count() != tactics.AutomaticTactics.Length ||
                tactics.AutomaticTactics.Any(id => !Tactics.TryGetValue(id, out var rule) || rule.Effect != TacticEffect.Martial ||
                    !rule.CompatibleFormations.Contains(formation.Id))))
                throw new ArgumentException("Invalid formation tactic profile.");
        if (Formations.Values.Any(f => f.RequiredTacticId is int id &&
            (!Tactics.TryGetValue(id, out var tactic) || tactic.Effect != TacticEffect.FormationTechnology)))
            throw new ArgumentException("Unknown formation technology reference.");
        foreach (var formation in Formations.Values) formation.Combat?.Validate();
        if (Formations.Values.Any(f => f.CaptureRanks.IsDefault || f.CaptureRanks.Any(r => !Enum.IsDefined(r)) ||
            (!f.CaptureRanks.IsEmpty || f.Attack is not null) && f.CaptureRanks.Length != f.OfficerCapacity))
            throw new ArgumentException("Missing formation capture ranks.");
        Places = definition.Places.ToImmutableDictionary(p => p.Id);
        if (Places.Values.Any(p => p.FacilityTypeId is int id && !Facilities.ContainsKey(id) ||
            !Facilities.IsEmpty && p.FacilityTypeId is null)) throw new ArgumentException("Unknown facility type reference.");
        foreach (var place in Places.Values) place.Combat?.Validate();
        if (Places.Values.Any(p => p.WoundedRecovery is < 0)) throw new ArgumentException("Invalid wounded recovery definition.");
        if (Formations.Values.Any(f => f.Attack is { Range: <= 0 } || f.Attack is not null && f.Combat is null) ||
            Places.Values.Any(p => p.Attack is { Range: <= 0 } || p.Attack is not null && p.Combat is null))
            throw new ArgumentException("Invalid ordinary attack definition.");
        if (Formations.Values.Any(f => f.Id < 0 || f.RequiredTacticId < 0 || f.MovementBudget <= 0 || f.DeploymentCost < 0 || f.SoldierCapacity == 0 ||
                f.OfficerCapacity <= 0 || !f.Footprint.IsValid || !Enum.IsDefined(f.TravelMode)) ||
            Places.Values.Any(p => p.Id < 0 || !p.Footprint.IsValid || new MapOccupancy(p.Position, p.Footprint) is var area &&
                (area.Left < 0 || area.Top < 0 || area.Right > Map.FineWidth || area.Bottom > Map.FineHeight)))
            throw new ArgumentException("Invalid military formation or place.");
    }

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
    public ImmutableDictionary<int, FormationRule> Formations { get; }
    public ImmutableDictionary<int, TacticRule> Tactics { get; }
    public ImmutableDictionary<int, BattlefieldPlace> Places { get; }
    public ImmutableDictionary<int, FacilityRule> Facilities { get; }
    public ImmutableDictionary<(int RegionId, WorldPosition Position), ConstructionSite> ConstructionSites { get; }

    public BattlefieldPlace Place(SettlementState settlement)
    {
        if (settlement.Facility is not { } instance) return Places[settlement.Id];
        var type = Facilities[instance.TypeId];
        return new(settlement.Id, instance.Position, type.Footprint)
        {
            FacilityTypeId = type.Id, CanDemolish = type.Buildable, WoundedRecovery = type.WoundedRecovery,
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
    [System.Text.Json.Serialization.JsonRequired] public ConstructionPlan? Construction { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public int? UnitId { get; init; }
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
