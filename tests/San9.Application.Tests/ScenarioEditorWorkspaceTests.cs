using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Authoring;
using San9.Content.Interface;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class ScenarioEditorWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "san9-scenario-editor-" + Guid.NewGuid().ToString("N"));
    private string Package => Path.Combine(_root, "scenario");
    private const string ScenarioId = "g01-public";

    private async Task Create(ScenarioInitialState? scenario = null)
    {
        scenario ??= G01ScenarioTests.Fixture();
        scenario = scenario with { Settlements = scenario.Settlements.Select(s => s with { Kind = SettlementKind.City }).ToArray() };
        var entries = scenario.Forces.Select(f => f.NameKey).Concat(scenario.Settlements.Select(s => s.NameKey))
            .Concat(scenario.Officers.Select(o => o.NameKey))
            .Concat(scenario.RegionNameKeys.Values).Distinct(StringComparer.Ordinal).ToDictionary(key => key, key => key);
        entries[TextCatalog.ScenarioTitleKey] = "原剧本";
        var text = new TextCatalog(TextCatalog.CurrentSchemaVersion, entries);
        var shared = SharedScenarioData.From(scenario, "shared/definitions.json", "shared/officers.json", "shared/geography.json", text);
        await ScenarioDataFiles.WriteSharedAsync(_root, shared);
        var content = new ScenarioContent(ScenarioContent.CurrentSchemaVersion, new("textures"),
            new(new(MapDefinition.CurrentSchemaVersion, "fixture", scenario.Military?.Width ?? 4, scenario.Military?.Height ?? 4,
                Enumerable.Repeat(new San9.Content.Maps.MapCellDefinition(0, 0, 0, null),
                    (scenario.Military?.Width ?? 4) * (scenario.Military?.Height ?? 4)).ToArray(), []), []), scenario, text, [])
        { Preview = new(ScenarioId, "historical", 190, 1, "原剧本", "原简介", new() { [1] = "color-1", [2] = "color-2" }) };
        await ScenarioContentFiles.WriteAsync(Package, content, resourceRoot: _root, sharedScenario: shared);
        var catalog = new ContentCatalog(ContentCatalog.CurrentSchemaVersion, "textures", "opening.json", [new(ScenarioId, "scenario")])
        { OfficerFlags = "flags.json", Definitions = shared.DefinitionsPath, OfficerDefaults = shared.OfficersPath };
        await File.WriteAllTextAsync(Path.Combine(_root, "catalog.json"), JsonSerializer.Serialize(catalog, PackageFiles.JsonOptions));
    }

    [Fact]
    public async Task PlayerScenarioReachesSelectionFullContentAndNewGameWhileSharedDefaultsRemainInherited()
    {
        await Create();
        var workspace = await ScenarioEditorWorkspace.OpenAsync(_root, ScenarioId);
        byte[] baseline = await File.ReadAllBytesAsync(workspace.ScenarioPath);
        workspace.SetDescription("自制[剧本]", "自制简介");
        workspace.Update(workspace.Document with
        {
            Year = 200, Month = 6, Day = 11,
            Forces = workspace.Document.Forces.Select(f => f.Id == 1 ? f with { Trust = 700 } : f with { ColorCode = 3 }).ToArray(),
            Settlements = workspace.Document.Settlements.Select(s => s.Id == 1 ? s with { Soldiers = 23000, Population = 50000 } : s).ToArray(),
            Officers = workspace.Document.Officers.Select(o => o.Id == 2 ? o with { OwnerId = 2, SettlementId = 2 } : o).ToArray()
        });
        await workspace.SaveAsync();
        Assert.False(workspace.HasChanges);
        Assert.Equal(baseline, await File.ReadAllBytesAsync(workspace.ScenarioPath));
        var preview = await ScenarioContentFiles.LoadPreviewAsync(Package);
        Assert.Equal((200, 6, "自制[剧本]", "自制简介"), (preview.Year, preview.Month, preview.Title, preview.Description));
        Assert.Equal("color-3", preview.CityColors[2]);
        var selection = await ScenarioContentFiles.LoadSelectionAsync(Package);
        var full = await ScenarioContentFiles.LoadAsync(Package);
        Assert.Equal(700, selection.Scenario.Forces.Single(f => f.Id == 1).Trust);
        Assert.Equal(23000u, full.Scenario.Settlements.Single(s => s.Id == 1).Soldiers);
        Assert.Equal(2, selection.Scenario.Officers.Single(o => o.State.Id == 2).State.OwnerId);
        Assert.Equal("自制[剧本]", selection.Text.Resolve(TextCatalog.ScenarioTitleKey));
        Assert.Equal("自制[剧本]", full.Text.Resolve(TextCatalog.ScenarioTitleKey));
        Assert.Equal(200, GameSession.Start(full.Scenario, 1).Snapshot.Clock.Year);

        string defaultsPath = Path.Combine(_root, "shared/officers.json");
        var defaults = await PackageFiles.ReadAsync<OfficerDefaultsCatalog>(defaultsPath);
        await File.WriteAllTextAsync(PlayerDefinitionFiles.PlayerPath(defaultsPath), JsonSerializer.Serialize(defaults with
        { Officers = defaults.Officers.Select(o => o.Id == 2 ? o with { War = 123 } : o).ToArray() }, ScenarioContentFiles.DataJsonOptions));
        var inherited = await ScenarioContentFiles.LoadSelectionAsync(Package);
        Assert.Equal(123, inherited.Scenario.Officers.Single(o => o.State.Id == 2).State.BaseAbilities.War);

        var reopened = await ScenarioEditorWorkspace.OpenAsync(_root, ScenarioId);
        Assert.Equal(200, reopened.Document.Year); Assert.False(reopened.HasChanges);
        reopened.Update(reopened.Document with { Month = 7 }); await reopened.SaveAsync();
        reopened.RestoreOpened(); Assert.Equal(6, reopened.Document.Month); Assert.True(reopened.HasChanges);
    }

    [Fact]
    public async Task InvalidDraftAndUnavailablePublicationLockRetainDraftWithoutPublishingEitherFile()
    {
        await Create();
        var workspace = await ScenarioEditorWorkspace.OpenAsync(_root, ScenarioId);
        workspace.Update(workspace.Document with { Officers = workspace.Document.Officers.Select(o => o.Id == 2 ? o with { OwnerId = 999 } : o).ToArray() });
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.SaveAsync());
        Assert.True(workspace.HasChanges); Assert.False(File.Exists(workspace.PlayerScenarioPath)); Assert.False(File.Exists(workspace.PlayerManifestPath));
        workspace.RestoreOpened(); workspace.Update(workspace.Document with { Year = 200 });
        using (var handle = new FileStream(workspace.PlayerManifestPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => workspace.SaveAsync());
        Assert.True(workspace.HasChanges); Assert.False(File.Exists(workspace.PlayerScenarioPath)); Assert.False(File.Exists(workspace.PlayerManifestPath));
        await workspace.SaveAsync(); Assert.False(workspace.HasChanges);
        Assert.Empty(Directory.GetFiles(Package, "*.pending"));
    }

    [Fact]
    public async Task ConcurrentEditorAndNewSharedPlayerCopyAreDetectedBeforePublication()
    {
        await Create();
        var first = await ScenarioEditorWorkspace.OpenAsync(_root, ScenarioId);
        var second = await ScenarioEditorWorkspace.OpenAsync(_root, ScenarioId);
        first.Update(first.Document with { Year = 200 }); await first.SaveAsync();
        second.Update(second.Document with { Year = 201 });
        await Assert.ThrowsAsync<InvalidDataException>(() => second.SaveAsync());
        Assert.True(second.HasChanges);
        Assert.Equal(200, (await ScenarioContentFiles.LoadPreviewAsync(Package)).Year);
        var third = await ScenarioEditorWorkspace.OpenAsync(_root, ScenarioId);
        third.Update(third.Document with { Year = 202 });
        string path = Path.Combine(_root, "shared/definitions.json");
        await File.WriteAllBytesAsync(PlayerDefinitionFiles.PlayerPath(path), await File.ReadAllBytesAsync(path));
        await Assert.ThrowsAsync<InvalidDataException>(() => third.SaveAsync());
        Assert.True(third.HasChanges);
    }

    [Fact]
    public async Task CompleteDataRejectsUnknownFieldsIdentityAndMapEditsWithoutReplacingDraft()
    {
        await Create();
        var workspace = await ScenarioEditorWorkspace.OpenAsync(_root, ScenarioId);
        var original = workspace.Document;
        var json = JsonNode.Parse(workspace.ExportJson())!;
        json["officers"]![0]!["misspelledField"] = 1;
        Assert.Throws<JsonException>(() => workspace.ApplyJson(json.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => workspace.Update(original with { ScenarioId = "other" }));
        Assert.Throws<InvalidDataException>(() => workspace.Update(original with { Settlements = original.Settlements.Select(s => s with { X = s.X + 1 }).ToArray() }));
        Assert.Same(original, workspace.Document); Assert.False(workspace.HasChanges);
    }

    [Fact]
    public async Task SharedCatalogDeletionChecksReferencesIntroducedByPlayerScenario()
    {
        await Create();
        var shared = await SharedDataEditorWorkspace.OpenAsync(_root);
        var office = await shared.CreateOfficeAsync(); await shared.SaveAsync();
        var workspace = await ScenarioEditorWorkspace.OpenAsync(_root, ScenarioId);
        workspace.Update(workspace.Document with
        { Officers = workspace.Document.Officers.Select(o => o.Id == 2 ? o with { OfficeId = office.Id } : o).ToArray() });
        await workspace.SaveAsync();
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.DeleteOfficeAsync(office.Id));
        Assert.Contains(reopened.Offices, o => o.Id == office.Id);
    }

    [Fact]
    public async Task PreviewRebuildUpdatesTheBaseEntryAndPreservesPlayerDescription()
    {
        await Create();
        var workspace = await ScenarioEditorWorkspace.OpenAsync(_root, ScenarioId);
        workspace.SetDescription("玩家标题", "玩家简介"); await workspace.SaveAsync();
        var baseline = await ScenarioContentFiles.ReadManifestAsync(Package, usePlayerManifest: false);
        await ScenarioContentFiles.WritePreviewAsync(Package, baseline.Preview! with { Title = "重建标题" });
        Assert.Equal("重建标题", (await ScenarioContentFiles.ReadManifestAsync(Package, usePlayerManifest: false)).Preview!.Title);
        Assert.Equal("玩家标题", (await ScenarioContentFiles.LoadPreviewAsync(Package)).Title);
    }

    private static ScenarioInitialState TreasureScenario()
    {
        var scenario = G01ScenarioTests.Fixture();
        return scenario with
        {
            RegionNameKeys = new() { [1] = "region.city", [701] = "region.remote" },
            RegionCityIds = new() { [1] = 1, [701] = 2 },
            UnbuiltPlaces = [new(701, MapCoordinates.Center(new(3, 3)))],
            Officers = scenario.Officers.Select(o => o.State.Id == 6 ? o with { State = o.State with { IsDormant = true, Status = OfficerStatus.Dead } } : o).ToArray(),
            Information = scenario.Information with
            {
                Treasures = [new(7001, "treasure.weapon", 1, 10, null) { AbilityBonuses = new(5, 0, 0, 0) },
                    new(7002, "treasure.book", 8, 0, null), new(7003, "treasure.disabled", 11, 0, null)],
                TreasureStates = [new(7001, 1, TreasureDiscovery.Discovered, 1), new(7002, null, TreasureDiscovery.Buried, 701),
                    new(7003, null, TreasureDiscovery.Disabled, null), new(90000, null, TreasureDiscovery.Disabled, 701)]
            }
        };
    }

    [Theory]
    [InlineData(TreasureDiscovery.Disabled)]
    [InlineData(TreasureDiscovery.Buried)]
    [InlineData(TreasureDiscovery.Discovered)]
    public async Task TreasureStateEditsReachNewGameAndRetainIndependentReferencesAndOtherStates(TreasureDiscovery discovery)
    {
        await Create(TreasureScenario());
        var workspace = await ScenarioEditorWorkspace.OpenAsync(_root, ScenarioId);
        var untouched = workspace.Document.Information.TreasureStates.Where(t => t.Id != 7001).ToArray();
        byte[] definitions = await File.ReadAllBytesAsync(Path.Combine(_root, "shared/definitions.json"));
        var edited = new TreasureState(7001, 6, discovery, 1);
        Assert.Contains(1, workspace.RegionIds); // Built places are valid treasure regions too.
        workspace.SetTreasureState(edited);
        await workspace.SaveAsync();
        var reopened = await ScenarioEditorWorkspace.OpenAsync(_root, ScenarioId);
        Assert.Equal(edited, reopened.GetTreasureState(7001));
        Assert.Equal(untouched, reopened.Document.Information.TreasureStates.Where(t => t.Id != 7001));
        Assert.Equal(definitions, await File.ReadAllBytesAsync(Path.Combine(_root, "shared/definitions.json")));
        var selection = await ScenarioContentFiles.LoadSelectionAsync(Package);
        var full = await ScenarioContentFiles.LoadAsync(Package);
        Assert.Equal(edited, selection.Scenario.Information.TreasureStates.Single(t => t.Id == 7001));
        Assert.Equal(edited, GameSession.Start(full.Scenario, 1).Snapshot.Treasures.Single(t => t.Id == 7001));
    }

    [Fact]
    public async Task TreasureEditingClearsReferencesEnablesDefinedItemsAndRejectsInvalidReferencesWithoutChangingDraft()
    {
        await Create(TreasureScenario());
        var workspace = await ScenarioEditorWorkspace.OpenAsync(_root, ScenarioId);
        var cleared = workspace.GetTreasureState(7001) with { HolderId = null, DiscoveryRegionId = null };
        workspace.SetTreasureState(cleared);
        workspace.SetTreasureState(workspace.GetTreasureState(7003) with { Discovery = TreasureDiscovery.Buried, DiscoveryRegionId = 701 });
        var draft = workspace.Document;
        foreach (var invalid in new[] { cleared with { Id = 90000 }, cleared with { HolderId = 999 },
            cleared with { DiscoveryRegionId = 999 }, cleared with { Discovery = (TreasureDiscovery)99 } })
        {
            Assert.Throws<InvalidDataException>(() => workspace.SetTreasureState(invalid));
            Assert.Same(draft, workspace.Document);
        }
        await workspace.SaveAsync();
        var reopened = await ScenarioEditorWorkspace.OpenAsync(_root, ScenarioId);
        Assert.Equal(cleared, reopened.GetTreasureState(7001));
        Assert.Equal(TreasureDiscovery.Buried, reopened.GetTreasureState(7003).Discovery);
        Assert.Equal(701, reopened.GetTreasureState(7003).DiscoveryRegionId);
    }

    [Fact]
    public async Task TreasureFormAndCompleteDataShareOneDraftAndOpeningRestoreBaseline()
    {
        await Create(TreasureScenario());
        var workspace = await ScenarioEditorWorkspace.OpenAsync(_root, ScenarioId);
        var original = workspace.GetTreasureState(7001);
        workspace.SetTreasureState(original with { HolderId = 2 });
        var json = JsonNode.Parse(workspace.ExportJson())!;
        var treasure = json["information"]!["treasureStates"]!.AsArray().Single(t => t!["id"]!.GetValue<int>() == 7001)!;
        Assert.Equal(2, treasure["holderId"]!.GetValue<int>());
        treasure["discoveryRegionId"] = 701;
        workspace.ApplyJson(json.ToJsonString());
        Assert.Equal(701, workspace.GetTreasureState(7001).DiscoveryRegionId);
        await workspace.SaveAsync();
        workspace.RestoreOpened();
        Assert.Equal(original, workspace.GetTreasureState(7001)); Assert.True(workspace.HasChanges);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
