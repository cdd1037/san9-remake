using System.Collections.Immutable;

namespace San9.Core.Domain;

public sealed record FormationRule(int Id, int MovementBudget, int DeploymentCost, uint SoldierCapacity,
    int OfficerCapacity, Footprint Footprint, TravelMode TravelMode);
public sealed record BattlefieldPlace(int Id, WorldPosition Position, Footprint Footprint);
public sealed record MilitaryDefinition(int Width, int Height, ImmutableArray<int> TerrainIds,
    ImmutableArray<TerrainPassage> Terrains, ImmutableArray<FormationRule> Formations,
    ImmutableArray<BattlefieldPlace> Places)
{
    [System.Text.Json.Serialization.JsonRequired] public ImmutableArray<MapCell> BlockedCells { get; init; } = [];
}

public sealed class MilitaryRulebook
{
    public MilitaryRulebook(MilitaryDefinition definition)
    {
        if (definition.TerrainIds.IsDefault || definition.Terrains.IsDefault || definition.Formations.IsDefault || definition.Places.IsDefault)
            throw new ArgumentException("Missing military definitions.");
        if (definition.BlockedCells.IsDefault) throw new ArgumentException("Missing battlefield blocking cells.");
        Map = new(definition.Width, definition.Height, definition.TerrainIds, definition.Terrains, definition.BlockedCells);
        Formations = definition.Formations.ToImmutableDictionary(f => f.Id);
        Places = definition.Places.ToImmutableDictionary(p => p.Id);
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

public enum MilitaryOrderKind { Move, Return, Transport }
public sealed record MilitaryOrder(MilitaryOrderKind Kind, WorldPosition? Position, int? SettlementId);
