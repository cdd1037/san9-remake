using System.Text.Json.Serialization;

namespace San9.Content.Maps;

[Flags]
public enum ConstructionSurface { None = 0, General = 1, Horizontal = 2, Vertical = 4 }

public enum BlockingSource { Stored, TerrainRule, Manual }

/// <summary>Authored cell inputs. Display masks, stamps and topology projections are rebuilt.</summary>
public sealed record MapCellDefinition(int Terrain, int Elevation, int TextureVariant, int? RegionId)
{
    public bool HideRegionName { get; init; }
    public int? BridgeOrientation { get; init; }
    public bool MovementBlocked { get; init; }
    public bool InfluenceBlocked { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public BlockingSource MovementSource { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public BlockingSource InfluenceSource { get; init; }
    public ConstructionSurface Construction { get; init; }
}

/// <summary>Terrain defaults used by explicit authoring operations.</summary>
public sealed record MapBlockingDefaults(int[] InfluenceBlockedTerrainIds, int[] HarborAccessTerrainIds, int HarborAccessRadius)
{
    public void Validate()
    {
        if (HarborAccessRadius < 0 || InfluenceBlockedTerrainIds.Any(id => id < 0) || HarborAccessTerrainIds.Any(id => id < 0) ||
            InfluenceBlockedTerrainIds.Distinct().Count() != InfluenceBlockedTerrainIds.Length ||
            HarborAccessTerrainIds.Distinct().Count() != HarborAccessTerrainIds.Length ||
            HarborAccessTerrainIds.Except(InfluenceBlockedTerrainIds).Any())
            throw new InvalidDataException("Invalid map blocking defaults.");
    }
}

/// <summary>Resource-theme recipe, independent of source executable layouts.</summary>
public sealed record TerrainLevel(int Elevation, int[] Resources);

public sealed record TerrainStyle(int Id, int Priority, bool Transitions, int[] OrderVariants, TerrainLevel[] Levels)
{
    public bool Water { get; init; }
    public bool BridgeWater { get; init; }
    public bool WaterEdge { get; init; }
    public bool ShoreEdge { get; init; }
    [JsonIgnore] public int MinimumElevation => Levels.Min(l => l.Elevation);
    [JsonIgnore] public int MaximumElevation => Levels.Max(l => l.Elevation);
    [JsonIgnore] public int VariantCount => OrderVariants.Length;

    public bool Supports(MapCellDefinition cell) => cell.Terrain == Id && cell.TextureVariant >= 0 &&
        cell.TextureVariant < VariantCount && Levels.Any(l => l.Elevation == cell.Elevation &&
            Enumerable.Range(0, 16).Any(mask => l.Resources[mask * VariantCount + cell.TextureVariant] >= 0));
}
