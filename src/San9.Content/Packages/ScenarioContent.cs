using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Content.Text;

namespace San9.Content.Packages;

/// <summary>Fixed geography and display placements shared by explicitly referencing scenarios.</summary>
public sealed record MapDefinition(int SchemaVersion, string SourceBuild,
    int Width, int Height, int CanvasWidth, int CanvasHeight, MapSquare[] Squares,
    TerrainStamp[] Terrain, MapSprite[] Bridges,
    MapSprite[] Clouds, int?[] LeftEdge, int?[] RightEdge,
    MapAnimatedSprite[] Water, MapSprite[] Boundaries, MapBuildingType[] BuildingTypes)
{
    public const int CurrentSchemaVersion = 1;
    public static MapDefinition From(MapPackage m) => new(CurrentSchemaVersion, m.SourceBuild,
        m.Width,m.Height,m.CanvasWidth,m.CanvasHeight,m.Squares,m.Terrain,m.Bridges,
        m.Clouds,m.LeftEdge,m.RightEdge,m.Animations.Water,m.Boundaries,m.BuildingTypes);
}

/// <summary>A scenario owns building placements and references a fixed map definition.</summary>
public sealed record MapInstance(MapDefinition Definition, MapBuildingPlacement[] Buildings)
{
    public static MapInstance From(MapPackage m) => new(MapDefinition.From(m), m.Buildings);

    public MapPackage Resolve(MapResources r, ScenarioInitialState scenario)
    {
        var d = Definition;
        if (d.SchemaVersion != MapDefinition.CurrentSchemaVersion) throw new InvalidDataException("Unsupported map definition version.");
        string season = scenario.Month switch
        {
            >= 1 and <= 3 => "spring", >= 4 and <= 6 => "summer",
            >= 7 and <= 9 => "autumn", >= 10 and <= 12 => "winter",
            _ => throw new InvalidDataException("Invalid initial map month.")
        };
        return new(MapPackage.CurrentSchemaVersion,d.SourceBuild,scenario.ScenarioId,season,d.Width,d.Height,
            d.CanvasWidth,d.CanvasHeight,d.Squares,r.Seasons,r.Assets,r.Masks,d.Terrain,d.Bridges,Buildings,
            r.Compositions,d.Clouds,d.LeftEdge,d.RightEdge,[],new(r.Clips,d.Water),d.Boundaries)
            { Units = r.Units.BindFormations(scenario.Information.Formations), BuildingTypes = d.BuildingTypes, MinimapColors = r.MinimapColors };
    }
}

public sealed record TextureDependency(string Path);

public sealed record ScenarioContent(int SchemaVersion, TextureDependency Textures,
    MapInstance Map, ScenarioInitialState Scenario, TextCatalog Text, Dictionary<string,string> IllustrationRoles)
{
    public San9.Content.Interface.ScenarioPreviewEntry? Preview { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public San9.Content.Interface.CommandPresentation Presentation { get; init; } = San9.Content.Interface.CommandPresentation.Empty;
    [System.Text.Json.Serialization.JsonIgnore] public string ResourceRoot { get; init; } = "";
    public const int CurrentSchemaVersion = 257;
}

/// <summary>Selected scenario data and static display references, read from the authoritative content files.</summary>
public sealed record ScenarioSelectionContent(ScenarioSelectionState Scenario, TextCatalog Text,
    Dictionary<string, string> IllustrationRoles);
