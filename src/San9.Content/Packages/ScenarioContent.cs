using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Content.Text;

namespace San9.Content.Packages;

/// <summary>Persisted map instance: all visual definitions are supplied by its texture dependency.</summary>
public sealed record MapInstance(string SourceBuild, string ScenarioId, string InitialSeason,
    int Width, int Height, int CanvasWidth, int CanvasHeight, MapSquare[] Squares,
    TerrainStamp[] Terrain, MapSprite[] Bridges, MapBuildingPlacement[] Buildings,
    MapSprite[] Clouds, int?[] LeftEdge, int?[] RightEdge, ContentSource[] Sources,
    MapAnimatedSprite[] Water, MapSprite[] Boundaries, MapBuildingType[] BuildingTypes)
{
    public static MapInstance From(MapPackage m) => new(m.SourceBuild, m.ScenarioId, m.InitialSeason,
        m.Width,m.Height,m.CanvasWidth,m.CanvasHeight,m.Squares,m.Terrain,m.Bridges,m.Buildings,
        m.Clouds,m.LeftEdge,m.RightEdge,m.Sources,m.Animations.Water,m.Boundaries,m.BuildingTypes);

    public MapPackage Resolve(MapResources r) => new(15,SourceBuild,ScenarioId,InitialSeason,Width,Height,
        CanvasWidth,CanvasHeight,Squares,r.Seasons,r.Assets,r.Masks,Terrain,Bridges,Buildings,
        r.Compositions,Clouds,LeftEdge,RightEdge,Sources,new(r.Clips,Water),Boundaries)
        { Units = r.Units, BuildingTypes = BuildingTypes, MinimapColors = r.MinimapColors };
}

public sealed record TextureDependency(string Path);

public sealed record ScenarioContent(int SchemaVersion, ContentSource[] Sources, TextureDependency Textures,
    MapInstance Map, ScenarioInitialState Scenario, TextCatalog Text, Dictionary<string,string> IllustrationRoles)
{
    [System.Text.Json.Serialization.JsonRequired]
    public San9.Content.Interface.CommandPresentation Presentation { get; init; } = San9.Content.Interface.CommandPresentation.Empty;
    public const int CurrentSchemaVersion = 178;
}
