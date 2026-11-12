using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Content.Maps;

/// <summary>Builds domain geography from the same cells used by the display compiler.</summary>
public static class MapGeographyCompiler
{
    private const int GeographyBuildVersion = 2;
    /// <summary>The cached network and grid projection depend only on immutable authored inputs.</summary>
    public static string InputHash(MapDefinition map, ScenarioGeography geography,
        ScenarioDocument document, ScenarioDefinitions definitions)
    {
        var input = new
        {
            AlgorithmVersion = GeographyBuildVersion,
            map.Width, map.Height,
            Cells = map.Cells.Select(c => new { c.Terrain, c.Elevation, c.RegionId, c.MovementBlocked,
                c.InfluenceBlocked, c.Construction }).ToArray(),
            RegionCities = geography.RegionCityIds.OrderBy(p => p.Key).ToArray(),
            Rules = document.DefinitionOverrides.Apply(definitions).Military,
            document.MilitaryPlaces, document.UnbuiltPlaces,
            Cities = document.Settlements.Where(s => s.Kind == SettlementKind.City).Select(s => s.Id).Order().ToArray(),
            geography.Military?.Geography.FootprintScale
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input, ScenarioContentFiles.DataJsonOptions)));
    }

    public static ScenarioGeography Resolve(ScenarioMapManifest manifest, MapDefinition map, ScenarioGeography geography,
        ScenarioDocument document, ScenarioDefinitions definitions, OfficerDefaultsCatalog officers, CancellationToken token = default) =>
        geography.Military is null || manifest.GeographyInputHash == InputHash(map, geography, document, definitions)
            ? geography : Compile(map, geography, document, definitions, officers, token);

    public static ScenarioGeography Compile(MapDefinition map, ScenarioGeography geography,
        ScenarioDocument document, ScenarioDefinitions definitions, OfficerDefaultsCatalog officers,
        CancellationToken token = default)
    {
        map.Validate();
        if (geography.Military is null) throw new InvalidDataException("地图缺少领域地理定义。");
        var effective = document.DefinitionOverrides.Apply(definitions);
        var facilities = (effective.Military ?? throw new InvalidDataException("地图缺少军事规则。")).Facilities;
        var sites = ConstructionSites(map, facilities.Where(f => f.Buildable).Select(f => (f.Id, f.Footprint)),
            document.MilitaryPlaces.Select(p => p.Id).Concat(document.UnbuiltPlaces.Select(p => p.Id)).ToHashSet());
        var military = geography.Military with
        {
            Width = map.Width, Height = map.Height,
            TerrainIds = map.Cells.Select(c => c.Terrain).ToImmutableArray(),
            Elevations = map.Cells.Select(c => c.Elevation).ToImmutableArray(),
            RegionIds = map.Cells.Select(c => c.RegionId).ToImmutableArray(),
            BlockedCells = map.Cells.Select((c, i) => (c, i)).Where(p => p.c.MovementBlocked)
                .Select(p => new MapCell(p.i % map.Width, p.i / map.Width)).ToImmutableArray(),
            Geography = geography.Military.Geography with
            {
                BlockedCells = map.Cells.Select((c, i) => (c, i)).Where(p => p.c.InfluenceBlocked)
                    .Select(p => new MapCell(p.i % map.Width, p.i / map.Width)).ToImmutableArray()
            },
            ConstructionSites = sites
        };
        var projected = geography with { Military = military };
        var scenario = document.Compile(definitions, officers.Officers, projected);
        var rulebook = new MilitaryRulebook(scenario.Military!);
        var harborTypes = facilities.Where(f => f.IsHarbor).Select(f => f.Id).ToHashSet();
        var harbors = scenario.Military!.Places.Where(p => p.FacilityTypeId is int type && harborTypes.Contains(type))
            .Select(p => rulebook.RegionAt(p.Position)).OfType<int>().ToHashSet();
        token.ThrowIfCancellationRequested();
        return projected with
        {
            CityConnections = CityConnections(map, geography.RegionCityIds, harbors,
                scenario.Military.Terrains.Where(t => t.IsWater).Select(t => t.Id).ToHashSet()),
            MilitaryCityConnections = MilitaryCityNetwork.Generate(rulebook,
                scenario.Settlements.Where(s => s.Kind == SettlementKind.City).Select(s => s.Id), token: token).Connections
        };
    }

    public static ImmutableArray<ConstructionSite> ConstructionSites(MapDefinition map,
        IEnumerable<(int Id, Footprint Footprint)> facilities, IReadOnlySet<int> configured)
    {
        map.Validate();
        var templates = facilities.ToArray();
        var sites = ImmutableArray.CreateBuilder<ConstructionSite>();
        for (int y = 1; y < map.Height - 1; y++)
        for (int x = 1; x < map.Width - 1; x++)
        {
            var cell = map.Cells[y * map.Width + x];
            if (cell.RegionId is not int region || !configured.Contains(region)) continue;
            bool CanBuild((int Id, Footprint Footprint) facility)
            {
                var shape = facility.Footprint;
                var surface = shape.Width > shape.Height ? ConstructionSurface.Horizontal :
                    shape.Height > shape.Width ? ConstructionSurface.Vertical : ConstructionSurface.General;
                int left = x - shape.Width / 2, top = y - shape.Height / 2;
                if (left < 0 || top < 0 || left + shape.Width > map.Width || top + shape.Height > map.Height) return false;
                for (int cy = top; cy < top + shape.Height; cy++)
                for (int cx = left; cx < left + shape.Width; cx++)
                {
                    var candidate = map.Cells[cy * map.Width + cx];
                    if (candidate.RegionId != region || (candidate.Construction & surface) == 0) return false;
                }
                return true;
            }
            var allowed = templates.Where(CanBuild).Select(f => f.Id).ToImmutableArray();
            if (!allowed.IsEmpty) sites.Add(new(region, MapCoordinates.Center(new(x, y)), allowed)
            {
                PreferenceDirections = ((cell.Construction & ConstructionSurface.Horizontal) != 0 ? ConstructionDirection.Horizontal : 0) |
                    ((cell.Construction & ConstructionSurface.Vertical) != 0 ? ConstructionDirection.Vertical : 0)
            });
        }
        return sites.ToImmutable();
    }

    public static ImmutableArray<CityConnection> CityConnections(MapDefinition map,
        IReadOnlyDictionary<int, int> regionCities, IReadOnlySet<int> portRegions, IReadOnlySet<int> waterTerrains)
    {
        map.Validate();
        HashSet<CityConnection> links = [];
        bool Eligible(int i) => map.Cells[i].RegionId is int region && regionCities.ContainsKey(region) &&
            !map.Cells[i].InfluenceBlocked && (!waterTerrains.Contains(map.Cells[i].Terrain) || portRegions.Contains(region));
        for (int index = 0; index < map.Cells.Length; index++)
        {
            if (!Eligible(index)) continue;
            int x = index % map.Width, y = index / map.Width;
            void Add(int next)
            {
                if (!Eligible(next)) return;
                int from = regionCities[map.Cells[index].RegionId!.Value], to = regionCities[map.Cells[next].RegionId!.Value];
                if (from != to) links.Add(new(from, to));
            }
            if (y > 0) Add(index - map.Width);
            if (x + 1 < map.Width) Add(index + 1);
            if (y + 1 < map.Height) Add(index + map.Width);
            if (x > 0) Add(index - 1);
        }
        return links.OrderBy(c => c.FromCityId).ThenBy(c => c.ToCityId).ToImmutableArray();
    }
}
