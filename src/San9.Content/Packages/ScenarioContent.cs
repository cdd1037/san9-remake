using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Content.Text;

namespace San9.Content.Packages;

/// <summary>Authoritative map inputs shared by explicitly referencing scenarios.</summary>
public sealed record MapDefinition(int SchemaVersion, string SourceBuild,
    int Width, int Height, MapCellDefinition[] Cells, MapBuildingType[] BuildingTypes)
{
    public const int CurrentSchemaVersion = 3;
    [System.Text.Json.Serialization.JsonRequired]
    public MapBlockingDefaults BlockingDefaults { get; init; } = new([], [], 2);

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion || Width <= 0 || Height <= 0 ||
            (long)Width * Height != Cells.Length)
            throw new InvalidDataException("Invalid map definition version or dimensions; rebuild the content package.");
        _ = MapDisplayMetrics.CanvasSize(Width, Height);
        BlockingDefaults.Validate();
        if (Cells.Any(c => c.Terrain < 0 || c.Elevation < 0 || c.TextureVariant < 0 ||
                c.RegionId < 0 || c.BridgeOrientation is < 0 or > 1 ||
                ((int)c.Construction & ~7) != 0) ||
            BuildingTypes.Select(t => t.Id).Distinct().Count() != BuildingTypes.Length)
            throw new InvalidDataException("Invalid map cell inputs or building types.");
    }
}

/// <summary>A scenario owns building placements and references an authored map.</summary>
public sealed record MapInstance(MapDefinition Definition, MapBuildingPlacement[] Buildings)
{
    public static MapInstance From(MapPackage map) => new(map.Authoring
        ?? throw new InvalidDataException("Map authoring inputs are required."), map.Buildings);

    public MapPackage Resolve(MapResources resources, ScenarioInitialState scenario) =>
        MapLayoutCompiler.Compile(Definition, Buildings, resources, scenario);
}

public sealed record TextureDependency(string Path);

public sealed record ScenarioContent(int SchemaVersion, TextureDependency Textures,
    MapInstance Map, ScenarioInitialState Scenario, TextCatalog Text, Dictionary<string,string> IllustrationRoles)
{
    public San9.Content.Interface.ScenarioPreviewEntry? Preview { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public San9.Content.Interface.CommandPresentation Presentation { get; init; } = San9.Content.Interface.CommandPresentation.Empty;
    [System.Text.Json.Serialization.JsonIgnore] public string ResourceRoot { get; init; } = "";
    public const int CurrentSchemaVersion = 263;
}

/// <summary>Selected scenario data and static display references, read from the authoritative content files.</summary>
public sealed record ScenarioSelectionContent(ScenarioSelectionState Scenario, TextCatalog Text,
    Dictionary<string, string> IllustrationRoles);
