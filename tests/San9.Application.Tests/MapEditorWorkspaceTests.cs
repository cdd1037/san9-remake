using System.Collections.Immutable;
using System.Text.Json;
using San9.Application.Authoring;
using San9.Content.Maps;
using San9.Content.Images;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed partial class MilitarySessionTests
{
    [Fact]
    public void TerrainReplacementKeepsSlopesAndExistingStylesAndRespectsThemeChoices()
    {
        const string original = "san9pk-tc-1.0.1.0-d20794ae";
        var levels = Enumerable.Range(0, 8).Select(h => new TerrainLevel(h, Enumerable.Repeat(10006, 48).ToArray())).ToArray();
        var forest = new MapTerrainPalette(new(0, 0, true, [0, 1, 2], levels), original);
        var low = new MapCellDefinition(2, 1, 2, 4) { BridgeOrientation = 0 };
        var high = low with { Elevation = 5 };
        Assert.Equal(low with { Terrain = 0, TextureVariant = 1 }, forest.ReplaceTerrain(low));
        Assert.Equal(high with { Terrain = 0, TextureVariant = 1 }, forest.ReplaceTerrain(high));
        var originalForest = high with { Terrain = 0 };
        Assert.Equal(originalForest, forest.ReplaceTerrain(originalForest));

        var style = new TerrainStyle(10, 0, false, [0, 1, 2], levels);
        var water = new MapTerrainPalette(style, original);
        Assert.Equal(1, water.ReplaceTerrain(high).Elevation);
        Assert.False(water.CommonVariant(1));
        Assert.Contains(1, water.VariantsAt(1));

        var custom = new MapTerrainPalette(style with { Levels = [levels[2], levels[6]] }, "custom");
        Assert.Equal(6, custom.ReplaceTerrain(high).Elevation);
        Assert.True(custom.CommonVariant(1));
        Assert.True(custom.CommonHeight(6));
    }

    [Fact]
    public async Task MapGeographyRetainsConstructionInConfiguredUnbuiltRegions()
    {
        string root = Path.Combine(Path.GetTempPath(), "san9-map-unbuilt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var baseline = Fixture();
            var source = baseline with
            {
                UnbuiltPlaces = [new(3, MapCoordinates.Center(new(3, 1)))],
                RegionNameKeys = new() { [3] = "unbuilt" }, RegionCityIds = new() { [3] = 1 },
                Military = baseline.Military! with
                {
                    Facilities = [new(0, 0, 100, new(1, 1), 0, false, true, true, 500000, null, null, null, []),
                        new(6, 1, 100, new(1, 1), 0, true, false, false, 1000, null, null, null, [])],
                    Places = baseline.Military.Places.Select(p => p with { FacilityTypeId = 0 }).ToImmutableArray()
                }
            };
            var workspace = await CreateMapEditor(root, source);
            var manifest = await ScenarioContentFiles.ReadManifestAsync(Path.Combine(root, "scenario"));
            var shared = await ScenarioDataFiles.LoadAsync(root, manifest);
            var geography = MapGeographyCompiler.Compile(workspace.Map, workspace.Geography, await SharedMapSites.ReadScenarioAsync<San9.Content.Scenarios.ScenarioDocument>(root, manifest, default), shared.Definitions, shared.Officers);
            var site = Assert.Single(geography.Military!.ConstructionSites.Where(s => s.RegionId == 3));
            Assert.Equal(MapCoordinates.Center(new(3, 1)), site.Position);
            Assert.Equal(6, Assert.Single(site.FacilityIds));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MapPublicationReopensThroughTheNormalLoaderAndRejectsStaleDrafts()
    {
        string root = Path.Combine(Path.GetTempPath(), "san9-map-publish-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = await CreateMapEditor(root);
            var stale = await MapEditorWorkspace.OpenSharedAsync(root);
            var resources = MapEditorResources();
            byte[] baseline = await File.ReadAllBytesAsync(Path.Combine(root, "scenario", "map-definition.json"));
            workspace.BeginStroke();
            workspace.Paint(new(3, 1), new(3, 1), 0, c => c with { Elevation = 2, MovementBlocked = true, InfluenceBlocked = true },
                resources.TerrainStyles.ToDictionary(s => s.Id));
            workspace.EndStroke();
            Assert.Throws<InvalidDataException>(() => workspace.MoveBuilding(1, new(7, 1)));
            workspace.MoveBuilding(1, new(2, 1));
            Assert.Equal(MapCoordinates.Center(new(2, 1)), MapCoordinates.Center(workspace.Sites.Sites[1].Cell));
            Assert.Equal(2, workspace.Buildings.Single(b => b.Id == 1).X);
            Assert.Equal(1, workspace.Map.Cells[10].RegionId);
            await workspace.SaveAsync(resources);
            Assert.False(workspace.HasChanges);
            Assert.Equal(baseline, await File.ReadAllBytesAsync(Path.Combine(root, "scenario", "map-definition.json")));
            var loaded = await ScenarioContentFiles.LoadAsync(Path.Combine(root, "scenario"));
            Assert.Equal(2, loaded.Scenario.Settlements.Single(s => s.Id == 1).X);
            Assert.Equal(2, loaded.Map.Definition.Cells[11].Elevation);
            Assert.Equal(2, loaded.Scenario.Military!.Elevations[11]);
            Assert.Contains(new MapCell(3, 1), loaded.Scenario.Military.BlockedCells);
            Assert.Contains(new MapCell(3, 1), loaded.Scenario.Military.Geography.BlockedCells);
            Assert.Equal(loaded.Map.Definition.Cells.Select(c => c.Terrain), loaded.Scenario.Military.TerrainIds);
            Assert.Equal(loaded.Map.Definition.Cells.Select(c => c.RegionId), loaded.Scenario.Military.RegionIds);
            _ = new ScenarioSessionDefinition(loaded.Scenario).CreateWorld(42);
            var reopened = await MapEditorWorkspace.OpenSharedAsync(root);
            Assert.False(reopened.HasChanges);
            Assert.Equal(workspace.Map.Cells, reopened.Map.Cells);
            await reopened.SaveAsync(resources);
            await Assert.ThrowsAsync<InvalidDataException>(() => stale.SaveAsync(resources));
            Assert.Equal(workspace.Map.Cells, (await MapEditorWorkspace.OpenSharedAsync(root)).Map.Cells);
            var changedCells = (MapCellDefinition[])workspace.Map.Cells.Clone();
            changedCells[11] = changedCells[11] with { MovementBlocked = false };
            await MapDefinitionFiles.WriteAsync(root, "scenario/map-definition.player.json", workspace.Map with { Cells = changedCells });
            var refreshed = await ScenarioContentFiles.LoadAsync(Path.Combine(root, "scenario"));
            Assert.DoesNotContain(new MapCell(3, 1), refreshed.Scenario.Military!.BlockedCells);
            Assert.False(File.Exists(Path.Combine(root, "scenario", "geography.player.json")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SharedSitesDriveBothScenarioStatesAndDisplayWithoutWritingScenarioFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "san9-map-shared-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = Fixture();
            source = source with
            {
                Forces = [source.Forces.Single() with { LeaderId = 1 }],
                Officers = [source.Officers.Single() with { State = source.Officers.Single().State with { Status = OfficerStatus.Ruler } }],
                Information = source.Information with { Corps = [source.Information.Corps.Single() with { LeaderId = 1 }] },
                Military = source.Military! with
                {
                    Facilities = [new(99, 0, 100, new(1, 1), 0, false, true, true, 500000, null, null, null, []) { IsHarbor = true }],
                    Places = source.Military.Places.Select(p => p with { FacilityTypeId = 99 }).ToImmutableArray()
                }
            };
            var first = await CreateMapEditor(root, source, new([0], [0], 1));
            var firstRoot = Path.Combine(root, "scenario");
            var otherRoot = Path.Combine(root, "other");
            Directory.CreateDirectory(otherRoot);
            var manifest = await ScenarioContentFiles.ReadManifestAsync(firstRoot);
            var peer = manifest with { Scenario = "other/scenario.json", Textures = new("../scenario/textures"),
                Preview = manifest.Preview! with { Id = "other" } };
            var catalog = await PackageFiles.ReadAsync<ContentCatalog>(Path.Combine(root, "catalog.json"));
            async Task Write(string path, object value) => await File.WriteAllBytesAsync(path,
                JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), ScenarioContentFiles.DataJsonOptions));
            await Write(Path.Combine(otherRoot, "content.json"), peer);
            await Write(Path.Combine(otherRoot, "scenario.json"), SharedMapSites.StateOnly((await SharedMapSites.ReadScenarioAsync<ScenarioDocument>(root, manifest, default)) with { ScenarioId = "other" }));
            await Write(Path.Combine(root, "catalog.json"), catalog with { Scenarios = [.. catalog.Scenarios, new("other", "other")] });

            first = await MapEditorWorkspace.OpenSharedAsync(root);
            var resources = MapEditorResources();
            var styles = resources.TerrainStyles.ToDictionary(s => s.Id);
            first.RegenerateBlocking();
            first.BeginStroke();
            first.Paint(new(3, 1), new(3, 1), 0, c => c with { Elevation = 2 }, styles);
            first.EndStroke();
            first.SetRegionCity(2, 1);
            await first.SaveAsync(resources);
            Assert.False(File.Exists(Path.Combine(firstRoot, "content.player.json")));
            Assert.False(File.Exists(Path.Combine(firstRoot, "scenario.player.json")));
            string mapPath = Path.Combine(firstRoot, "map-definition.player.json");
            byte[] sharedBeforeMove = await File.ReadAllBytesAsync(mapPath);
            first.MoveBuilding(1, new(4, 1));
            await first.SaveAsync(resources);
            Assert.False(File.Exists(Path.Combine(firstRoot, "scenario.player.json")));
            Assert.False(File.Exists(Path.Combine(otherRoot, "scenario.player.json")));
            var moved = await ScenarioContentFiles.LoadAsync(firstRoot);
            var other = await ScenarioContentFiles.LoadAsync(otherRoot);
            Assert.Equal(2, other.Map.Definition.Cells[11].Elevation);
            Assert.Equal(1, other.Scenario.RegionCityIds[2]);
            Assert.Equal(1, moved.Map.Definition.Cells[12].RegionId);
            Assert.Equal(1, other.Map.Definition.Cells[12].RegionId);
            Assert.False(moved.Map.Definition.Cells[12].InfluenceBlocked);
            Assert.False(other.Map.Definition.Cells[12].InfluenceBlocked);
            Assert.Equal(4, other.Map.Buildings.Single(b => b.Id == 1).X);
            Assert.Equal(4, moved.Map.Buildings.Single(b => b.Id == 1).X);
            var selection = await ScenarioContentFiles.LoadSelectionAsync(otherRoot);
            Assert.Equal(4, selection.Scenario.Settlements.Single(s => s.Id == 1).X);
            var scenarioEditor = await ScenarioEditorWorkspace.OpenAsync(root, "other");
            var document = scenarioEditor.Document;
            scenarioEditor.Update(document with { Settlements = document.Settlements.Select(s =>
                s.Id == 1 ? s with { Soldiers = 123 } : s).ToArray() });
            await scenarioEditor.SaveAsync();
            var player = await PackageFiles.ReadAsync<JsonElement>(Path.Combine(otherRoot, "scenario.player.json"));
            Assert.False(player.GetProperty("settlements")[0].TryGetProperty("x", out _));
            Assert.False(player.GetProperty("militaryPlaces")[0].TryGetProperty("position", out _));
            var edited = (await ScenarioContentFiles.LoadAsync(otherRoot)).Scenario.Settlements.Single(s => s.Id == 1);
            Assert.Equal(123u, edited.Soldiers);
            Assert.Equal(new MapCell(4, 1), edited.Cell);
            Assert.Equal(other.Map.Definition.Cells, (await MapEditorWorkspace.OpenSharedAsync(root)).Map.Cells);

            // A shared manual exception still wins inside this scenario's moved harbor radius.
            first.BeginStroke();
            first.Paint(new(3, 1), new(3, 1), 0, c => c with { InfluenceBlocked = true }, styles, paintBlocking: true);
            first.EndStroke();
            await first.SaveAsync(resources);
            Assert.True((await ScenarioContentFiles.LoadAsync(firstRoot)).Map.Definition.Cells[11].InfluenceBlocked);
            sharedBeforeMove = await File.ReadAllBytesAsync(mapPath);

            first.BeginStroke();
            first.Paint(new(4, 1), new(4, 1), 0, c => c with { RegionId = 2 }, styles);
            first.EndStroke();
            var failure = await Assert.ThrowsAsync<InvalidDataException>(() => first.SaveAsync(resources));
            Assert.Contains("锚点", failure.Message);
            Assert.Equal(sharedBeforeMove, await File.ReadAllBytesAsync(mapPath));
            Assert.True(first.HasChanges);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OpeningSharedMapDoesNotReadScenarioEntriesOrWorldState()
    {
        string root = Path.Combine(Path.GetTempPath(), "san9-shared-open-" + Guid.NewGuid().ToString("N"));
        try
        {
            await CreateMapEditor(root);
            File.Delete(Path.Combine(root, "scenario", "content.json"));
            File.Delete(Path.Combine(root, "scenario", "scenario.json"));
            var workspace = await MapEditorWorkspace.OpenSharedAsync(root);
            workspace.MoveBuilding(1, new(3, 1));
            Assert.Equal(new MapCell(3, 1), workspace.Sites.Sites[1].Cell);
            workspace.Undo();
            Assert.False(workspace.HasChanges);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MapStrokesValidateInputsAndUndoTerrainWithItsBlockingChanges()
    {
        string root = Path.Combine(Path.GetTempPath(), "san9-map-blocking-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = await CreateMapEditor(root, blockingDefaults: new([1], [], 2));
            var styles = MapEditorResources().TerrainStyles.Append(new TerrainStyle(1, 1, true, [0],
                [new(1, Enumerable.Repeat(10006, 16).ToArray())])).ToDictionary(s => s.Id);
            var before = workspace.Snapshot();
            workspace.BeginStroke();
            Assert.Equal(2, workspace.Paint(new(1, 1), new(2, 1), 0, cell => cell with { Terrain = 1 }, styles));
            workspace.EndStroke();
            Assert.True(workspace.Map.Cells[10].InfluenceBlocked);
            Assert.True(workspace.Map.Cells[1].MovementBlocked);
            Assert.False(workspace.Map.Cells[7].MovementBlocked);
            Assert.True(workspace.Map.Cells[11].MovementBlocked);
            workspace.Undo();
            Assert.False(workspace.HasChanges);
            Assert.Equal(before.Cells, workspace.Map.Cells);
            workspace.Redo();
            Assert.True(workspace.Map.Cells[1].MovementBlocked);
            workspace.Undo();
            workspace.BeginStroke();
            Assert.Throws<InvalidDataException>(() => workspace.Paint(new(0, 0), new(7, 0), 0,
                cell => cell with { Elevation = cell.RegionId == 1 ? 2 : 9 }, styles));
            workspace.CancelStroke();
            Assert.False(workspace.HasChanges);
            Assert.True(workspace.RegenerateBlocking() > 0);
            Assert.True(workspace.Map.Cells[7].MovementBlocked);
            Assert.False(workspace.Map.Cells[9].MovementBlocked);
            Assert.Equal(0, workspace.RegenerateBlocking());
            workspace.Undo();
            Assert.Equal(before.Cells, workspace.Map.Cells);
            Assert.False(workspace.HasChanges);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MovingAHarborUpdatesOldAndNewShoreBlockingAndPreservesRemoteOverrides()
    {
        string root = Path.Combine(Path.GetTempPath(), "san9-map-harbor-blocking-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = Fixture();
            source = source with
            {
                Military = source.Military! with
                {
                    Facilities = [new(99, 0, 100, new(1, 1), 0, false, true, true, 500000, null, null, null, [])
                        { IsHarbor = true }],
                    Places = source.Military.Places.Select(p => p with { FacilityTypeId = 99 }).ToImmutableArray()
                }
            };
            var workspace = await CreateMapEditor(root, source, new([0], [0], 1));
            workspace.BeginStroke();
            workspace.Paint(new(5, 1), new(5, 1), 0, cell => cell with { MovementBlocked = false },
                MapEditorResources().TerrainStyles.ToDictionary(s => s.Id));
            workspace.EndStroke();
            var before = workspace.Snapshot();
            Assert.False(before.Cells[8].InfluenceBlocked);
            Assert.True(before.Cells[11].InfluenceBlocked);
            workspace.MoveBuilding(1, new(2, 1));
            Assert.True(workspace.Map.Cells[8].InfluenceBlocked);
            Assert.False(workspace.Map.Cells[11].InfluenceBlocked);
            Assert.False(workspace.Map.Cells[10].MovementBlocked);
            Assert.Equal(before.Cells[13], workspace.Map.Cells[13]);
            var moved = workspace.Snapshot();
            workspace.Undo();
            Assert.Equal(before.Cells, workspace.Map.Cells);
            Assert.Equal(new MapCell(0, 1), workspace.Sites.Sites[1].Cell);
            workspace.Redo();
            Assert.Equal(moved.Cells, workspace.Map.Cells);
            Assert.Equal(new MapCell(2, 1), workspace.Sites.Sites[1].Cell);
            workspace.MoveBuilding(1, new(5, 1));
            Assert.False(workspace.Map.Cells[14].InfluenceBlocked); // Within both harbor radii.
            workspace.MoveBuilding(1, new(0, 1));
            Assert.False(workspace.Map.Cells[14].InfluenceBlocked); // The second harbor still covers this tile.

            var rules = await SharedDataEditorWorkspace.OpenAsync(root);
            rules.SetFacilityHarbor(99, false);
            await rules.SaveAsync();
            var disabled = await ScenarioContentFiles.LoadAsync(Path.Combine(root, "scenario"));
            Assert.True(disabled.Map.Definition.Cells[14].InfluenceBlocked);
            Assert.Contains(new MapCell(6, 1), disabled.Scenario.Military!.Geography.BlockedCells);
            rules.SetFacilityHarbor(99, true);
            await rules.SaveAsync();
            Assert.False((await ScenarioContentFiles.LoadAsync(Path.Combine(root, "scenario"))).Map.Definition.Cells[14].InfluenceBlocked);
        }
        finally { Directory.Delete(root, true); }
    }

    private static MapResources MapEditorResources()
    {
        ImageAsset Asset(string id) => new(id, "fixture/" + id + ".png", 1, 1);
        var assets = new List<ImageAsset> { Asset("cloud-fill") };
        assets.AddRange(Enumerable.Range(10794, 12).Select(id => Asset($"boundary-{id}")));
        foreach (int resource in Enumerable.Range(5663, 312))
            foreach (string transparency in new[] { "alpha", "key" }) assets.Add(Asset($"building-{resource}-{transparency}"));
        var masks = Enumerable.Range(10806, 162).Select(id => new MapMask(id, $"weight-{id}", $"coverage-{id}")).ToArray();
        assets.AddRange(masks.SelectMany(m => new[] { Asset(m.WeightAsset), Asset(m.CoverageAsset) }));
        var seasons = new[] { "spring", "summer", "autumn", "winter" }.Select(id =>
            new MapSeason(id, Enumerable.Range(10006, 771).Select(resource =>
                new ImageAsset(resource.ToString(), $"fixture/{id}/{resource}.png", 1, 1)).ToArray())).ToArray();
        var clips = Enumerable.Range(0, 51).Select(c => $"flag-cloth-{c}")
            .Concat(Enumerable.Range(0, 2).SelectMany(v => Enumerable.Range(0, 186).Select(g => $"flag-glyph-{v}-{g}")))
            .Concat(Enumerable.Range(0, 8).Select(d => $"soldier-{d}"))
            .Concat(["flag-pole-back", "flag-pole-front"])
            .Select(id => new MapAnimationClip(id, ["cloud-fill"], 100, [])).ToArray();
        return new(seasons, assets.ToArray(), masks,
            [new(9, [new(false, false, 5663, 0, 0)])], clips, new int[288], new int[16], [], new byte[32])
        {
            TerrainStyles = [new(0, 1, true, [0], [new(1, Enumerable.Repeat(10006, 16).ToArray()), new(2, Enumerable.Repeat(10007, 16).ToArray())])]
        };
    }
    private static async Task<MapEditorWorkspace> CreateMapEditor(string root, ScenarioInitialState? source = null,
        MapBlockingDefaults? blockingDefaults = null)
    {
        var baseline = source ?? Fixture();
        var scenario = baseline with
        {
            Settlements = baseline.Settlements.Select(s => s with { Kind = SettlementKind.City }).ToArray(),
            RegionNameKeys = new(baseline.RegionNameKeys) { [1] = "origin", [2] = "destination" },
            RegionCityIds = new(baseline.RegionCityIds) { [1] = 1, [2] = 2 }
        };
        var text = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["force"] = "势力", ["origin"] = "甲城", ["destination"] = "乙城", ["officer"] = "武将", ["unbuilt"] = "空白地域" });
        var map = new MapDefinition(MapDefinition.CurrentSchemaVersion, "fixture", 8, 3,
            Enumerable.Range(0, 24).Select(i => new MapCellDefinition(0, 1, 0,
                baseline.UnbuiltPlaces.FirstOrDefault(p => MapCoordinates.Cell(p.Position) == new MapCell(i % 8, i / 8))?.Id ??
                (i % 8 < 4 ? 1 : 2)) { Construction = ConstructionSurface.General }).ToArray(), [new(0, 100)])
        { BlockingDefaults = blockingDefaults ?? new([], [], 2) };
        var content = new ScenarioContent(ScenarioContent.CurrentSchemaVersion, new("textures"),
            new(map, [new(1, 0, 1, 0), new(2, 7, 1, 0)]), scenario, text, [])
        { Preview = new(scenario.ScenarioId, "custom", 190, 1, "地图测试", "地图测试", []) };
        const string locations = "scenario/places.json";
        var sites = content.Map.Buildings.ToDictionary(b => b.Id, b =>
        {
            var place = scenario.Military!.Places.Single(p => p.Id == b.Id);
            return new MapSite($"site {b.Id}", new(b.X, b.Y), place.Footprint, place.FacilityTypeId, true) { DisplayType = b.FixedType };
        });
        foreach (var place in scenario.UnbuiltPlaces)
            sites.Add(place.Id, new($"site {place.Id}", MapCoordinates.Cell(place.Position), new(1, 1), null, false));
        await ScenarioContentFiles.WriteAsync(Path.Combine(root, "scenario"), content, resourceRoot: root, locations: locations);
        await File.WriteAllBytesAsync(Path.Combine(root, locations), JsonSerializer.SerializeToUtf8Bytes(new SharedMapSites(sites, []), PackageFiles.JsonOptions));
        var catalog = new ContentCatalog(ContentCatalog.CurrentSchemaVersion, "scenario/textures", "opening.json", [new(scenario.ScenarioId, "scenario")])
        { OfficerFlags = "flags.json", Definitions = "scenario/definitions.json", OfficerDefaults = "scenario/officer-defaults.json",
            SharedMap = new("scenario/map-definition.json", "scenario/geography.json", locations) };
        await File.WriteAllTextAsync(Path.Combine(root, "catalog.json"), JsonSerializer.Serialize(catalog, PackageFiles.JsonOptions), System.Text.Encoding.UTF8);
        return await MapEditorWorkspace.OpenSharedAsync(root);
    }
}
