using System.Collections.Immutable;
using System.Text.Json;
using San9.Application.Authoring;
using San9.Application.Presentation;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class TreasureDataEditorWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "san9-treasure-data-" + Guid.NewGuid().ToString("N"));

    private async Task Create(bool treasuresOnly = false)
    {
        var source = treasuresOnly ? new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "a", 190, 1, 1, null, [], [], [], [])
            : PresentationTacticFixture.WithInfantry(G01ScenarioTests.Fixture());
        source = source with { Information = source.Information with
        {
            Treasures = [new(901, "宝物甲", 1, 5, null) { Description = "原说明", AbilityBonuses = new(3, 0, 0, 0) },
                new(902, "宝物乙", 8, 0, null)],
            TreasureStates = [new(901, treasuresOnly ? null : 1, TreasureDiscovery.Discovered, null), new(902, null, TreasureDiscovery.Disabled, null)]
        } };
        var entries = source.Forces.Select(f => f.NameKey).Concat(source.Settlements.Select(s => s.NameKey))
            .Concat(source.Officers.Select(o => o.NameKey)).Distinct().ToDictionary(key => key, key => key);
        var text = new TextCatalog(TextCatalog.CurrentSchemaVersion, entries);
        var shared = SharedScenarioData.From(source, "shared/definitions.json", "shared/officers.json", "shared/geography.json", text);
        await ScenarioDataFiles.WriteSharedAsync(_root, shared);
        foreach (string id in new[] { "a", "private" })
        {
            var world = source with { ScenarioId = id };
            if (id == "private") world = world with { Information = source.Information with
            { Treasures = source.Information.Treasures.Select(t => t.Id == 901 ? t with { Name = "剧本专属" } : t).ToArray() } };
            var content = new ScenarioContent(ScenarioContent.CurrentSchemaVersion, new("textures"),
                new(new(MapDefinition.CurrentSchemaVersion, "fixture", 4, 4, 4, 4, [], [], [], [], [], [], [], [], []), []), world, text, [])
            { Preview = new(id, "custom", world.Year, world.Month, "测试剧本", "测试简介", []) };
            await ScenarioContentFiles.WriteAsync(Path.Combine(_root, id), content, resourceRoot: _root, sharedScenario: shared);
        }
        var catalog = new ContentCatalog(ContentCatalog.CurrentSchemaVersion, "textures", "opening.json", [new("a", "a"), new("private", "private")])
        { OfficerFlags = "flags.json", Definitions = shared.DefinitionsPath, OfficerDefaults = shared.OfficersPath };
        await File.WriteAllTextAsync(Path.Combine(_root, "catalog.json"), JsonSerializer.Serialize(catalog, PackageFiles.JsonOptions));
    }

    [Fact]
    public async Task SharedTreasureEditsReachRulesTextAndViewsWhilePrivateOverridesAndInitialOwnershipPersist()
    {
        await Create();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var original = workspace.GetTreasure(901);
        var originalContent = await ScenarioContentFiles.LoadAsync(Path.Combine(_root, "a"));
        Assert.Null(TreasureDetails.Read(GameSession.Start(originalContent.Scenario, 1).View, originalContent.Text, 901).PortraitRole);
        var edited = original with { Name = "自制[宝物]", Description = "第一行\n[第二行]", ImageKey = "mod/treasures/custom", Category = 0,
            AbilityBonuses = new(7, 8, 9, 10), LoyaltyBonus = 17, TacticId = 2, EscapeMount = true,
            RetentionGroup = TreasureRetentionGroup.Intelligence, AutomaticDiplomacyOfferAllowed = false, FavoredOwnerIds = [2] };
        workspace.SetTreasure(edited); await workspace.SaveAsync();
        Assert.False(workspace.HasChanges);
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.True(ContentComparison.Equal(edited, reopened.GetTreasure(901)));
        var content = await ScenarioContentFiles.LoadAsync(Path.Combine(_root, "a"));
        var session = GameSession.Start(content.Scenario, 1);
        var item = session.Snapshot.Treasures.Single(t => t.Id == 901);
        Assert.Equal(1, item.HolderId); Assert.Equal(TreasureDiscovery.Discovered, item.Discovery);
        Assert.Equal(edited.AbilityBonuses, new TreasureRulebook(content.Scenario.Information.Treasures.Select(t => t.ToRule())).HeldBonuses(session.Snapshot.Treasures, 1));
        var detail = TreasureDetails.Read(session.View, content.Text, 901);
        Assert.Equal(edited.Name, detail.Name);
        Assert.Equal(edited.ImageRole, detail.PortraitRole);
        Assert.Equal(edited.ImageKey, content.Scenario.Information.Treasures.Single(t => t.Id == 901).ImageKey);
        Assert.Equal(MessageMarkup.Escape(edited.Description), detail.Description);
        Assert.Equal("统率+10、武力+7、智力+8、政治+9", detail.Ability);
        Assert.Equal("安然撤退", detail.Effect);
        Assert.Equal(content.Scenario.Information.Tactics.Single(t => t.Id == 2).Name, detail.Tactic);
        Assert.False(content.Text.Entries.ContainsKey("treasure.901"));
        Assert.False(content.Text.Entries.ContainsKey("treasure.901.description"));
        Assert.Equal("剧本专属", (await ScenarioContentFiles.LoadAsync(Path.Combine(_root, "private"))).Scenario.Information.Treasures.Single(t => t.Id == 901).Name);
        workspace.RestoreTreasure(901);
        Assert.True(ContentComparison.Equal(original, workspace.GetTreasure(901)));
        Assert.True(workspace.HasChanges);
    }

    [Fact]
    public async Task TreasureOnlyLibraryAndTextEditsUseLightweightOpeningAndLocalValidation()
    {
        await Create(treasuresOnly: true);
        File.Delete(Path.Combine(_root, "shared/geography.json"));
        File.Delete(Path.Combine(_root, "a/scenario.json"));
        File.Delete(Path.Combine(_root, "private/scenario.json"));
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        workspace.SetTreasure(workspace.GetTreasure(901) with { Name = "独立宝物", Description = "正文", ImageKey = "mod/treasures/independent" });
        await workspace.SaveAsync();
        var reopened = (await SharedDataEditorWorkspace.OpenAsync(_root)).GetTreasure(901);
        Assert.Equal("独立宝物", reopened.Name);
        Assert.Equal("mod/treasures/independent", reopened.ImageKey);
    }

    [Fact]
    public async Task TreasureCanReferenceAnInternalTacticWithNoDisplayCatalogEntry()
    {
        await Create();
        string path = Path.Combine(_root, "shared/definitions.json");
        var definitions = await PackageFiles.ReadAsync<ScenarioDefinitions>(path);
        definitions = definitions with { Information = definitions.Information with
        { Tactics = definitions.Information.Tactics.Where(t => t.Id != 2).ToArray() } };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(definitions, ScenarioContentFiles.DataJsonOptions));
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        workspace.SetTreasure(workspace.GetTreasure(901) with { TacticId = 2 }); await workspace.SaveAsync();
        var content = await ScenarioContentFiles.LoadAsync(Path.Combine(_root, "a"));
        var session = GameSession.Start(content.Scenario, 1);
        Assert.Equal("兵法 [2]", TreasureDetails.Read(session.View, content.Text, 901).Tactic);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("image")]
    [InlineData("category")]
    [InlineData("bonus")]
    [InlineData("loyalty")]
    [InlineData("tactic")]
    [InlineData("owner")]
    public async Task InvalidTreasureDataCannotPublishAndKeepsDraft(string field)
    {
        await Create();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var treasure = workspace.GetTreasure(901);
        treasure = field switch
        {
            "name" => treasure with { Name = " " },
            "image" => treasure with { ImageKey = " " },
            "category" => treasure with { Category = 13 },
            "bonus" => treasure with { AbilityBonuses = new(-1, 0, 0, 0) },
            "loyalty" => treasure with { LoyaltyBonus = -1 },
            "tactic" => treasure with { TacticId = 9999 },
            _ => treasure with { FavoredOwnerIds = [9999] }
        };
        workspace.SetTreasure(treasure);
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.SaveAsync());
        Assert.True(workspace.HasChanges); Assert.False(File.Exists(workspace.PlayerDefinitionPath));
        Assert.True(ContentComparison.Equal(treasure, workspace.GetTreasure(901)));
    }

    [Fact]
    public async Task NewAndCopiedTreasuresUseIndependentInitialStateAndCanBeEnabledThroughTheScenarioEditor()
    {
        await Create();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var source = workspace.GetTreasure(901) with { ImageKey = "mod/treasures/source", FavoredOwnerIds = [2] };
        workspace.SetTreasure(source);
        var created = await workspace.CreateTreasureAsync();
        Assert.Equal(11, created.Category);
        Assert.Null(created.ImageKey);
        Assert.Null(created.TacticId);
        Assert.Equal(default, created.AbilityBonuses);
        var copy = await workspace.CreateTreasureAsync(source.Id);
        Assert.True(created.Id > 902 && copy.Id > created.Id);
        Assert.Equal(source with { Id = copy.Id, Name = source.Name + "副本" }, copy);
        Assert.True(workspace.IsTreasureChanged(copy.Id));
        await workspace.SaveAsync();
        workspace.SetTreasure(copy with { Name = "改名" });
        workspace.RestoreTreasure(copy.Id);
        Assert.Equal(copy, workspace.GetTreasure(copy.Id));
        var content = await ScenarioContentFiles.LoadAsync(Path.Combine(_root, "a"));
        Assert.Equal(new TreasureState(copy.Id, null, TreasureDiscovery.Disabled, null),
            content.Scenario.Information.TreasureStates.Single(t => t.Id == copy.Id));
        var selection = await ScenarioContentFiles.LoadSelectionAsync(Path.Combine(_root, "a"));
        Assert.Equal(content.Scenario.Information.TreasureStates, selection.Scenario.Information.TreasureStates);
        Assert.DoesNotContain((await ScenarioContentFiles.LoadAsync(Path.Combine(_root, "private"))).Scenario.Information.Treasures, t => t.Id == copy.Id);
        var scenario = await ScenarioEditorWorkspace.OpenAsync(_root, "a");
        Assert.Equal(TreasureDiscovery.Disabled, scenario.GetTreasureState(copy.Id).Discovery);
        Assert.DoesNotContain(scenario.Document.Information.TreasureStates, t => t.Id == copy.Id);
        var state = new TreasureState(copy.Id, 1, TreasureDiscovery.Discovered, null);
        scenario.SetTreasureState(state);
        await scenario.SaveAsync();
        content = await ScenarioContentFiles.LoadAsync(Path.Combine(_root, "a"));
        var session = GameSession.Start(content.Scenario, 1);
        Assert.Equal(state, session.Snapshot.Treasures.Single(t => t.Id == copy.Id));
        Assert.Equal(copy.Name, TreasureDetails.Read(session.View, content.Text, copy.Id).Name);
        Assert.DoesNotContain("nextTreasureId", System.Text.Encoding.UTF8.GetString(San9.Application.Persistence.SaveService.Encode(session)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExistingUnusedTreasuresCanBeDeletedAndDeletedIdentitiesStayReservedAfterSaving()
    {
        await Create();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        await workspace.DeleteTreasureAsync(902);
        await workspace.SaveAsync();
        var created = await workspace.CreateTreasureAsync();
        await workspace.DeleteTreasureAsync(created.Id);
        await workspace.SaveAsync();
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.DoesNotContain(reopened.Treasures, t => t.Id == 902 || t.Id == created.Id);
        Assert.True((await reopened.CreateTreasureAsync()).Id > created.Id);
        Assert.Equal(TreasureDiscovery.Disabled, (await ScenarioContentFiles.LoadAsync(Path.Combine(_root, "a")))
            .Scenario.Information.TreasureStates.Single(t => t.Id == 902).Discovery);
    }

    [Fact]
    public async Task ActiveTreasureDeletionIsRejectedAndLateScenarioReferencesAreRecheckedAtSave()
    {
        await Create();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var draft = workspace.Definitions;
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => workspace.DeleteTreasureAsync(901));
        Assert.Contains("剧本 a", error.Message);
        Assert.Contains("901", error.Message);
        Assert.Same(draft, workspace.Definitions);
        await workspace.DeleteTreasureAsync(902);
        await EditWorld("a", document => document with { Information = document.Information with
        { TreasureStates = document.Information.TreasureStates.Select(t => t.Id == 902 ? t with { Discovery = TreasureDiscovery.Discovered, HolderId = 1 } : t).ToArray() } });
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.SaveAsync());
        Assert.False(File.Exists(workspace.PlayerDefinitionPath));
        Assert.DoesNotContain(workspace.Treasures, t => t.Id == 902);
    }

    [Fact]
    public async Task SharedRuleReferencesBlockDeletionEvenWhenScenariosReplaceTheCatalog()
    {
        await Create();
        string path = Path.Combine(_root, "shared/definitions.json");
        var definitions = await PackageFiles.ReadAsync<ScenarioDefinitions>(path);
        await Write("shared/definitions.json", definitions with { ImperialSealId = 902 });
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var draft = workspace.Definitions;
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => workspace.DeleteTreasureAsync(902));
        Assert.Contains("902", error.Message);
        Assert.Same(draft, workspace.Definitions);
    }

    [Fact]
    public async Task UnsavedTreasureDeletionChecksMessagesAndSaveRechecksLateMessageReferences()
    {
        await Create();
        var manifest = await ScenarioContentFiles.ReadManifestAsync(Path.Combine(_root, "a"));
        await Write("a/content.json", manifest with { Messages = ["treasure-reference.json"] });
        await Write("treasure-reference.json", new Dictionary<string, MessageDefinition>());
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var created = await workspace.CreateTreasureAsync();
        var messages = new Dictionary<string, MessageDefinition>
        { ["treasure-reference"] = new([new MessageValue(new("entity", "Treasure", created.Id))]) };
        await Write("treasure-reference.json", messages);
        var draft = workspace.Definitions;
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => workspace.DeleteTreasureAsync(created.Id));
        Assert.Contains("treasure-reference", error.Message);
        Assert.Same(draft, workspace.Definitions);
        await Write("treasure-reference.json", new Dictionary<string, MessageDefinition>());
        await workspace.DeleteTreasureAsync(created.Id);
        await Write("treasure-reference.json", messages);
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.SaveAsync());
        Assert.False(File.Exists(workspace.PlayerDefinitionPath));
    }

    [Fact]
    public async Task TreasureAllocationReservesPrivateDefinitionsAndDisabledStateIdentities()
    {
        await Create();
        var basis = await SharedDataEditorWorkspace.OpenAsync(_root);
        await EditWorld("private", document => document with { DefinitionOverrides = document.DefinitionOverrides with
        { Information = new(basis.Definitions.Information with { Treasures = [.. basis.Treasures, new(5000, "私有宝物", 11, 0, null)] }) } });
        await EditWorld("a", document => document with { Information = document.Information with
        { TreasureStates = [.. document.Information.TreasureStates, new(8000, null, TreasureDiscovery.Disabled, null)] } });
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.True((await workspace.CreateTreasureAsync()).Id > 8000);
    }

    [Fact]
    public async Task ExhaustedTreasureIdentitiesAndCancellationLeaveTheDraftUntouched()
    {
        await Create();
        string path = Path.Combine(_root, "shared/definitions.json");
        var definitions = await PackageFiles.ReadAsync<ScenarioDefinitions>(path);
        await Write("shared/definitions.json", definitions with { NextTreasureId = (long)int.MaxValue + 1 });
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var draft = workspace.Definitions;
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.CreateTreasureAsync());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workspace.CreateTreasureAsync(901, cancellation.Token));
        Assert.Same(draft, workspace.Definitions);
        Assert.False(workspace.HasChanges);
    }

    [Fact]
    public async Task AnEmptyTreasureCatalogCanReopenAndCreateAgain()
    {
        await Create(treasuresOnly: true);
        foreach (string id in new[] { "a", "private" })
            await EditWorld(id, document => document with { Information = document.Information with
            { TreasureStates = document.Information.TreasureStates.Select(t => t with { Discovery = TreasureDiscovery.Disabled }).ToArray() } });
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        await workspace.DeleteTreasureAsync(901);
        await workspace.DeleteTreasureAsync(902);
        await workspace.SaveAsync();
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.Empty(reopened.Treasures);
        Assert.True((await reopened.CreateTreasureAsync()).Id > 902);
    }

    private async Task EditWorld(string id, Func<ScenarioDocument, ScenarioDocument> change)
    {
        var document = await PackageFiles.ReadAsync<ScenarioDocument>(Path.Combine(_root, id, "scenario.json"));
        await Write(id + "/scenario.json", change(document));
    }

    private Task Write<T>(string relative, T value) => File.WriteAllTextAsync(Path.Combine(_root, relative),
        JsonSerializer.Serialize(value, ScenarioContentFiles.DataJsonOptions), System.Text.Encoding.UTF8);

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
