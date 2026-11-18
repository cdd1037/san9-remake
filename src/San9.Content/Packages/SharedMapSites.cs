using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Content.Packages;

public sealed record SharedMapReference(string Definition, string Geography, string Locations);
public sealed record MapSite(string Name, MapCell Cell, Footprint Footprint, int? FacilityType, bool IsCity)
{
    public int? DisplayType { get; init; } = FacilityType;
}
public sealed record SharedMapSites(Dictionary<int, MapSite> Sites, Dictionary<string, string> Labels)
{
    public MapBuildingPlacement[] Buildings(IEnumerable<int> ids) => ids.Select(id =>
    {
        var site = Sites[id];
        return new MapBuildingPlacement(id, site.Cell.X, site.Cell.Y, site.DisplayType ?? -1);
    }).ToArray();

    public void Validate(MapDefinition map)
    {
        var occupied = new List<MapOccupancy>();
        foreach (var (id, site) in Sites)
        {
            var cell = site.Cell;
            if (cell.X < 0 || cell.Y < 0 || cell.X >= map.Width || cell.Y >= map.Height || !site.Footprint.IsValid)
                throw new InvalidDataException($"地点 {id} 的位置或占地无效。");
            var area = new MapOccupancy(MapCoordinates.Center(cell), site.Footprint);
            if (area.Left < 0 || area.Top < 0 || area.Right > (long)map.Width * MapCoordinates.UnitsPerCell ||
                area.Bottom > (long)map.Height * MapCoordinates.UnitsPerCell || occupied.Any(area.Overlaps))
                throw new InvalidDataException($"地点 {id} 的占地越界或重叠。");
            occupied.Add(area);
        }
    }

    // The wire document owns scenario state. Runtime records also carry resolved coordinates.
    public static JsonElement StateOnly(ScenarioDocument document)
    {
        var node = JsonSerializer.SerializeToNode(document, ScenarioContentFiles.DataJsonOptions)!.AsObject();
        foreach (var site in node["settlements"]!.AsArray()) { site!.AsObject().Remove("x"); site.AsObject().Remove("y"); }
        foreach (var field in new[] { "militaryPlaces", "unbuiltPlaces" })
            foreach (var site in node[field]!.AsArray()) site!.AsObject().Remove("position");
        return JsonSerializer.SerializeToElement(node);
    }

    public T Resolve<T>(JsonElement state, bool includeMilitary = true)
    {
        var node = JsonObject.Create(state)!;
        foreach (var item in node["settlements"]!.AsArray())
        {
            var cell = Require(item!).Cell;
            item!["x"] = cell.X; item["y"] = cell.Y;
        }
        if (includeMilitary)
            foreach (var field in new[] { "militaryPlaces", "unbuiltPlaces" })
                foreach (var item in node[field]!.AsArray())
                    item!["position"] = JsonSerializer.SerializeToNode(MapCoordinates.Center(Require(item!).Cell), ScenarioContentFiles.DataJsonOptions);
        var value = node.Deserialize<T>(ScenarioContentFiles.DataJsonOptions) ?? throw new InvalidDataException("Empty scenario.");
        ContentStructure.Validate(value);
        return value;
    }

    private MapSite Require(JsonNode item)
    {
        int id = item["id"]!.GetValue<int>();
        return Sites.TryGetValue(id, out var site) ? site : throw new InvalidDataException($"共享地图缺少地点 {id}。");
    }

    public static async Task<T> ReadScenarioAsync<T>(string root, ScenarioContentManifest manifest, CancellationToken token, bool includeMilitary = true)
    {
        string path = PlayerDefinitionFiles.ActivePath(PackageFiles.Resolve(root, manifest.Scenario));
        if (manifest.Locations is null) return await PackageFiles.ReadAsync<T>(path, token);
        var sites = await PackageFiles.ReadAsync<SharedMapSites>(PlayerDefinitionFiles.ActivePath(PackageFiles.Resolve(root, manifest.Locations)), token);
        return sites.Resolve<T>(await PackageFiles.ReadAsync<JsonElement>(path, token), includeMilitary);
    }
}
