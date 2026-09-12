using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed record FormationRule(int Id, int MovementBudget, int DeploymentCost, uint SoldierCapacity,
    int OfficerCapacity, Footprint Footprint, TravelMode TravelMode)
{
    [System.Text.Json.Serialization.JsonRequired] public CombatProfile? Combat { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public OrdinaryAttack? Attack { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public ImmutableArray<BattleRank> CaptureRanks { get; init; } = [];
}

public sealed record OrdinaryAttack(int Range, bool DamagesWalls);

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
    [System.Text.Json.Serialization.JsonRequired] public CombatProfile? Combat { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public OrdinaryAttack? Attack { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public bool DestroyedAtZeroDurability { get; init; }
}
public sealed record MilitaryDefinition(int Width, int Height, ImmutableArray<int> TerrainIds,
    ImmutableArray<TerrainPassage> Terrains, ImmutableArray<FormationRule> Formations,
    ImmutableArray<BattlefieldPlace> Places)
{
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
        Formations = definition.Formations.ToImmutableDictionary(f => f.Id);
        foreach (var formation in Formations.Values) formation.Combat?.Validate();
        if (Formations.Values.Any(f => f.CaptureRanks.IsDefault || f.CaptureRanks.Any(r => !Enum.IsDefined(r)) ||
            (!f.CaptureRanks.IsEmpty || f.Attack is not null) && f.CaptureRanks.Length != f.OfficerCapacity))
            throw new ArgumentException("Missing formation capture ranks.");
        Places = definition.Places.ToImmutableDictionary(p => p.Id);
        foreach (var place in Places.Values) place.Combat?.Validate();
        if (Formations.Values.Any(f => f.Attack is { Range: <= 0 } || f.Attack is not null && f.Combat is null) ||
            Places.Values.Any(p => p.Attack is { Range: <= 0 } || p.Attack is not null && p.Combat is null))
            throw new ArgumentException("Invalid ordinary attack definition.");
        if (Formations.Values.Any(f => f.Id < 0 || f.MovementBudget <= 0 || f.DeploymentCost < 0 || f.SoldierCapacity == 0 ||
                f.OfficerCapacity <= 0 || !f.Footprint.IsValid || !Enum.IsDefined(f.TravelMode)) ||
            Places.Values.Any(p => p.Id < 0 || !p.Footprint.IsValid || new MapOccupancy(p.Position, p.Footprint) is var area &&
                (area.Left < 0 || area.Top < 0 || area.Right > Map.FineWidth || area.Bottom > Map.FineHeight)))
            throw new ArgumentException("Invalid military formation or place.");
    }

    public BattlefieldMap Map { get; }
    public ImmutableDictionary<int, FormationRule> Formations { get; }
    public ImmutableDictionary<int, BattlefieldPlace> Places { get; }
}

public enum MilitaryOrderKind { Move, Return, Transport, AttackUnit, AttackSettlement }
public sealed record MilitaryOrder(MilitaryOrderKind Kind, WorldPosition? Position, int? SettlementId)
{
    [System.Text.Json.Serialization.JsonRequired] public int? UnitId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public bool IsAttack => Kind is MilitaryOrderKind.AttackUnit or MilitaryOrderKind.AttackSettlement;
    [System.Text.Json.Serialization.JsonIgnore] public bool IsValid => Enum.IsDefined(Kind) && (Kind switch
    {
        MilitaryOrderKind.Move => Position is { X: >= 0, Y: >= 0 } && SettlementId is null && UnitId is null,
        MilitaryOrderKind.AttackUnit => Position is null && SettlementId is null && UnitId is >= 0,
        _ => Position is null && SettlementId is >= 0 && UnitId is null
    });
}
