using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Content.Packages;

public sealed record ScenarioMapManifest(string Definition, MapBuildingPlacement[] Buildings)
{
    public string? GeographyInputHash { get; init; }

    public MapDefinition ApplyLayout(MapDefinition map, ScenarioDocument scenario, ScenarioDefinitions definitions)
    {
        var layout = map;
        var settlements = scenario.Settlements.ToDictionary(s => s.Id);
        foreach (var building in Buildings)
        {
            if (!settlements.TryGetValue(building.Id, out var site) || map.Cells.Length == 0) continue;
            if (site.X < 0 || site.Y < 0 || site.X >= map.Width || site.Y >= map.Height)
                throw new InvalidDataException("据点位置超出地图。");
            int index = site.Y * map.Width + site.X;
            if (layout.Cells[index].RegionId == site.Id) continue;
            if (ReferenceEquals(layout, map)) layout = map with { Cells = (MapCellDefinition[])map.Cells.Clone() };
            layout.Cells[index] = layout.Cells[index] with { RegionId = site.Id };
        }

        var types = (scenario.DefinitionOverrides.Apply(definitions).Military?.Facilities ?? [])
            .Where(f => f.IsHarbor).Select(f => f.Id).ToHashSet();
        var ports = scenario.MilitaryPlaces.Where(p => p.FacilityTypeId is int type && types.Contains(type))
            .Select(p => MapCoordinates.Cell(p.Position));
        return ResolveBlocking(layout, ports);
    }

    public static MapDefinition ResolveBlocking(MapDefinition map, IEnumerable<MapCell> ports)
    {
        var shores = map.BlockingDefaults.HarborAccessTerrainIds.ToHashSet();
        var selected = Enumerable.Range(0, map.Cells.Length).Where(i =>
            shores.Contains(map.Cells[i].Terrain) || map.Cells[i].InfluenceSource == BlockingSource.TerrainRule).ToArray();
        if (selected.Length == 0 && !map.Cells.Any(c => c.MovementSource == BlockingSource.TerrainRule)) return map;
        return map with { Cells = MapBlockingGenerator.Generate(map, ports, selected, resolveLayout: true) };
    }

}
public sealed record SharedMapDefinition(string Path, MapDefinition Definition);

public static class MapDefinitionFiles
{
    public static async Task<SharedMapDefinition> WriteAsync(string resourceRoot, string path, MapDefinition definition,
        CancellationToken token = default, ContentPublication? publication = null)
    {
        if (definition.SchemaVersion != MapDefinition.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported map definition version.");
        using var own = publication is null ? new ContentPublication() : null;
        await (publication ?? own!).StageAsync(PackageFiles.Resolve(resourceRoot, path), definition, ScenarioContentFiles.DataJsonOptions, token);
        own?.Commit(token);
        return new(path, definition);
    }
}
