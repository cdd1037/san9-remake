using System.Collections.Immutable;
using System.Text.Json;
using San9.Application.Authoring;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed partial class SharedDataEditorWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "san9-formations-" + Guid.NewGuid().ToString("N"));

    private async Task Create()
    {
        var formation = new FormationRule(7000, 50, 12, 100000, 2, new(1, 1), TravelMode.Land)
        { Positions = [new(0, BattleRank.Front), new(1, BattleRank.Middle)], Vision = new(5, 180) };
        var scenario = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "a", 190, 1, 1, null, [], [], [], [])
        {
            Military = new(2, 2, [0, 0, 0, 0], [new(0, 10, null)], [formation], []),
            Information = new([new(8000, "自制爵位", 15000, 100, 5, 0)], [new(9000, "自制官职", 12000, 20)], [], [], []) { Formations = [new(7000, "自制阵形")
            { Presentation = new(2, 3, [new(0, 0), new(1, 1)], 1, 180, "background", "available") }] }
        };
        scenario.Validate();
        var shared = SharedScenarioData.From(scenario, "shared/definitions.json", "shared/officers.json", "shared/geography.json", new(TextCatalog.CurrentSchemaVersion, []));
        await ScenarioDataFiles.WriteSharedAsync(_root, shared);
        foreach (string id in new[] { "a", "b", "private" })
        {
            var world = scenario with { ScenarioId = id };
            if (id == "private") world = world with { Military = world.Military! with { Formations = [formation with { MovementBudget = 70 }] } };
            var content = new ScenarioContent(ScenarioContent.CurrentSchemaVersion, new("../textures"),
                new(new(MapDefinition.CurrentSchemaVersion, "fixture", 2, 2,
                    Enumerable.Repeat(new San9.Content.Maps.MapCellDefinition(0, 0, 0, null), 4).ToArray(), []), []), world,
                new(TextCatalog.CurrentSchemaVersion, new() { ["shape"] = "自制阵形", ["office"] = "自制官职" }), []);
            await ScenarioContentFiles.WriteAsync(Path.Combine(_root, id), content, resourceRoot: _root, sharedScenario: shared);
        }
        var catalog = new ContentCatalog(ContentCatalog.CurrentSchemaVersion, "textures", "opening.json", [new("a", "a"), new("b", "b"), new("private", "private")])
        { OfficerFlags = "flags.json", Definitions = shared.DefinitionsPath, OfficerDefaults = shared.OfficersPath };
        await File.WriteAllTextAsync(Path.Combine(_root, "catalog.json"), JsonSerializer.Serialize(catalog, PackageFiles.JsonOptions));
    }

    private async Task CreatePeopleAndTactics()
    {
        await Create();
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["fixture-person"] = "试将" });
        var person = new OfficerInitialState("fixture-person", new(9001, null, null, new(70, 60, 50, 80), false)
        { Status = OfficerStatus.Dead, BirthYear = 150, AppearanceYear = 165, Surname = "试", LearnedTactics = [6000] });
        var profile = OfficerDefaults.From(person, texts);
        await File.WriteAllTextAsync(Path.Combine(_root, "shared/officers.json"), JsonSerializer.Serialize(
            new OfficerDefaultsCatalog(OfficerDefaultsCatalog.CurrentSchemaVersion, [profile]), ScenarioContentFiles.DataJsonOptions));
        var definitions = JsonSerializer.Deserialize<ScenarioDefinitions>(await File.ReadAllTextAsync(Path.Combine(_root, "shared/definitions.json")), ScenarioContentFiles.DataJsonOptions)!;
        definitions = definitions with
        {
            Information = definitions.Information with { Tactics = [new(6000, "试兵法") { Description = "原说明" }] },
            Military = definitions.Military! with { Tactics = [new(6000, 0, TacticEffect.Martial, 10, 5, 100, true, false, [7000])] }
        };
        await File.WriteAllTextAsync(Path.Combine(_root, "shared/definitions.json"), JsonSerializer.Serialize(definitions, ScenarioContentFiles.DataJsonOptions));
        foreach (string id in new[] { "a", "b", "private" })
        {
            string path = Path.Combine(_root, id, "scenario.json");
            var document = JsonSerializer.Deserialize<ScenarioDocument>(await File.ReadAllTextAsync(path), ScenarioContentFiles.DataJsonOptions)!;
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document with
            {
                Officers = [ScenarioOfficerState.From(person, profile, texts, definitions.DefaultLoyalty)],
                DefinitionOverrides = document.DefinitionOverrides.Military is { Value: { } privateMilitary }
                    ? document.DefinitionOverrides with { Military = new(privateMilitary with { Tactics = definitions.Military!.Tactics }) }
                    : document.DefinitionOverrides
            }, ScenarioContentFiles.DataJsonOptions));
        }
    }

    [Fact]
    public async Task PersonalOfficersAreSavedSeparatelyAndAvailableAtGameStart()
    {
        await CreatePeopleAndTactics();
        string path = Path.Combine(_root, "user", "player-officers.json");
        var editor = await SharedDataEditorWorkspace.OpenAsync(_root, createdOfficersPath: path);
        var person = await editor.CreateOfficerAsync();
        await editor.SaveAsync();
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(editor.PlayerOfficerPath));
        Assert.False(File.Exists(editor.PlayerDefinitionPath));
        var package = Path.Combine(_root, "a");
        var scenario = await ScenarioContentFiles.LoadScenarioAsync(package, createdOfficersPath: path);
        Assert.Contains(person.Id, scenario.OfficerCatalog.Keys);
        Assert.DoesNotContain(person.Id, (await ScenarioContentFiles.LoadScenarioAsync(package)).OfficerCatalog.Keys);
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root, createdOfficersPath: path);
        var stale = await SharedDataEditorWorkspace.OpenAsync(_root, createdOfficersPath: path);
        var original = reopened.GetOfficer(9001);
        reopened.SetOfficer(original with { War = 80 });
        await reopened.DeleteOfficerAsync(person.Id);
        await reopened.SaveAsync();
        var shared = await PackageFiles.ReadAsync<OfficerDefaultsCatalog>(editor.PlayerOfficerPath);
        Assert.Equal(80, Assert.Single(shared.Officers).War);
        stale.SetOfficer(person with { War = 60 });
        await Assert.ThrowsAsync<InvalidDataException>(() => stale.SaveAsync());
        var next = await SharedDataEditorWorkspace.OpenAsync(_root, createdOfficersPath: path);
        Assert.DoesNotContain(next.Officers, p => p.Id == person.Id);
        Assert.True((await next.CreateOfficerAsync()).Id > person.Id);
    }

    [Fact]
    public async Task BasicMilitaryRulesPersistAndRespectScenarioOverrides()
    {
        await Create();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var links = workspace.Definitions.Military!.LinkRules;
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.SetDefensiveTacticChance(101));
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.SetKinshipLinkBonus(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.SetCityInfluence(new(-1, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.SetCityInfluence(new(8, 101)));
        Assert.False(workspace.HasChanges);
        workspace.SetDefensiveTacticChance(65);
        workspace.SetKinshipLinkBonus(30);
        workspace.SetCityInfluence(new(6, 15));
        await workspace.SaveAsync();
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.Equal(links with { KinshipBonus = 30 }, reopened.Definitions.Military!.LinkRules);
        foreach (string id in new[] { "a", "private" })
        {
            var manifest = JsonSerializer.Deserialize<ScenarioContentManifest>(await File.ReadAllTextAsync(Path.Combine(_root, id, "content.json")), PackageFiles.JsonOptions)!;
            var scenario = await ScenarioDataFiles.LoadScenarioAsync(_root, manifest);
            var rules = new MilitaryRulebook(scenario.Military!);
            Assert.Equal(id == "a" ? 65 : 0, rules.DefensiveTacticChance);
            Assert.Equal(id == "a" ? new CityInfluenceRule(6, 15) : null, rules.CityInfluence);
            Assert.Equal(id == "a" ? 30 : links.KinshipBonus, rules.LinkRules.KinshipBonus);
        }
    }

    [Fact]
    public async Task ProficiencyCapPersistsInPlayerDefinitionsWithoutChangingBaseOrOfficerValues()
    {
        await CreatePeopleAndTactics();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var before = workspace.GetOfficer(9001);
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.SetTacticProficiencyCap(0));
        workspace.SetTacticProficiencyCap(1500);
        workspace.SetOfficer(before with { TacticProficiencies = before.TacticProficiencies.SetItem(0, 1500) });
        await workspace.SaveAsync();
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.Equal(1500, reopened.Definitions.OfficerGrowth.TacticProficiencyCap);
        Assert.Equal(1500, reopened.GetOfficer(9001).TacticProficiencies[0]);
        var baseline = JsonSerializer.Deserialize<ScenarioDefinitions>(await File.ReadAllTextAsync(workspace.DefinitionPath), ScenarioContentFiles.DataJsonOptions)!;
        Assert.Equal(1000, baseline.OfficerGrowth.TacticProficiencyCap);
        reopened.SetTacticProficiencyCap(600);
        reopened.SetOfficer(reopened.GetOfficer(9001) with { Surname = "改" });
        Assert.Equal(1500, reopened.GetOfficer(9001).TacticProficiencies[0]);
    }

    [Fact]
    public async Task UnplacedOfficerBiographyIsValidatedBeforeEitherPlayerDocumentIsPublished()
    {
        await Create();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var person = await workspace.CreateOfficerAsync();
        workspace.SetOfficer(person with { Biography = "[preset=blue-outline]未闭合" });
        workspace.SetOffice(workspace.GetOffice(9000) with { Name = "待保存" });
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.SaveAsync());
        Assert.False(File.Exists(workspace.PlayerDefinitionPath));
        Assert.False(File.Exists(workspace.PlayerOfficerPath));
        Assert.True(workspace.HasChanges);
        workspace.SetOfficer(person with { Biography = "[preset=blue-outline]有效列传[/preset]" });
        await workspace.SaveAsync();
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.Equal("待保存", reopened.GetOffice(9000).Name);
        Assert.Equal("[preset=blue-outline]有效列传[/preset]", reopened.GetOfficer(person.Id).Biography);
    }

    [Fact]
    public async Task CreatingAndCopyingPeoplePreservesOriginalIdentityAndDoesNotDeployThem()
    {
        await CreatePeopleAndTactics();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var original = workspace.GetOfficer(9001);
        var created = await workspace.CreateOfficerAsync();
        var copy = await workspace.CreateOfficerAsync(original.Id);
        Assert.True(workspace.IsCreatedOfficer(created.Id));
        Assert.True(workspace.IsCreatedOfficer(copy.Id));
        Assert.False(workspace.IsCreatedOfficer(original.Id));
        Assert.NotEqual(original.NameKey, copy.NameKey);
        Assert.Equal(original.LearnedTactics, copy.LearnedTactics);
        Assert.Equal(original.ParentId, copy.ParentId);
        Assert.Equal(original.BloodlineId, copy.BloodlineId);
        Assert.Equal(original, workspace.GetOfficer(original.Id));
        await workspace.SaveAsync();
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.True(reopened.IsCreatedOfficer(created.Id));
        Assert.True(reopened.IsCreatedOfficer(copy.Id));
        Assert.False(reopened.IsCreatedOfficer(original.Id));
        Assert.Equal(9001, Assert.Single((await ScenarioContentFiles.LoadAsync(Path.Combine(_root, "a"))).Scenario.Officers).State.Id);
        reopened.SetOfficer(reopened.GetOfficer(created.Id) with { War = 88 });
        reopened.RestoreOfficer(created.Id);
        Assert.True(ContentComparison.Equal(created, reopened.GetOfficer(created.Id)));
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => reopened.DeleteOfficerAsync(original.Id));
        Assert.Contains("原有武将", failure.Message);
        Assert.True(ContentComparison.Equal(original, reopened.GetOfficer(original.Id)));
    }

    [Theory]
    [InlineData(10001)]
    [InlineData(20000)]
    public async Task NewIdentitiesRespectTheBaseCatalogEvenWhenThePlayerCopyOmitsAnUnusedBasePerson(long baseNextId)
    {
        await CreatePeopleAndTactics();
        string path = Path.Combine(_root, "shared", "officers.json");
        var original = JsonSerializer.Deserialize<OfficerDefaultsCatalog>(await File.ReadAllTextAsync(path), ScenarioContentFiles.DataJsonOptions)!;
        var baseline = original with { Officers = [.. original.Officers, OfficerDefaults.Create(10000)], NextOfficerId = baseNextId };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(baseline, ScenarioContentFiles.DataJsonOptions));
        await File.WriteAllTextAsync(PlayerDefinitionFiles.PlayerPath(path), JsonSerializer.Serialize(
            original with { NextOfficerId = 10000 }, ScenarioContentFiles.DataJsonOptions));
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var created = await workspace.CreateOfficerAsync();
        Assert.True(created.Id >= baseNextId);
        Assert.True(workspace.IsCreatedOfficer(created.Id));
        Assert.DoesNotContain(workspace.Officers, p => p.Id == 10000);
        await workspace.SaveAsync();
    }

    [Fact]
    public async Task SavedDeletionNeverRecyclesAnAuthoredOfficerIdentity()
    {
        await CreatePeopleAndTactics();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var created = await workspace.CreateOfficerAsync();
        await workspace.SaveAsync();
        await workspace.DeleteOfficerAsync(created.Id);
        Assert.True(workspace.HasChanges);
        await workspace.SaveAsync();
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        var next = await reopened.CreateOfficerAsync();
        Assert.True(next.Id > created.Id);
        Assert.DoesNotContain(reopened.Officers, p => p.Id == created.Id);
    }

    [Fact]
    public async Task ParentAndBloodlineReferencesBlockDeletionWithoutChangingTheDraft()
    {
        await CreatePeopleAndTactics();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var parent = await workspace.CreateOfficerAsync();
        var child = await workspace.CreateOfficerAsync();
        workspace.SetOfficer(child with { ParentId = parent.Id, BloodlineId = parent.Id });
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => workspace.DeleteOfficerAsync(parent.Id));
        Assert.Contains("亲代", failure.Message); Assert.Contains("血缘", failure.Message);
        Assert.Equal(parent, workspace.GetOfficer(parent.Id));
        workspace.SetOfficer(child);
        await workspace.DeleteOfficerAsync(parent.Id);
        Assert.DoesNotContain(workspace.Officers, p => p.Id == parent.Id);
    }

    [Fact]
    public async Task NewIdentitiesAvoidScenarioOnlyPeopleAndUnresolvedFamilyReferences()
    {
        await CreatePeopleAndTactics();
        string path = Path.Combine(_root, "a", "scenario.json");
        var document = JsonSerializer.Deserialize<ScenarioDocument>(await File.ReadAllTextAsync(path), ScenarioContentFiles.DataJsonOptions)!;
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document with
        { AdditionalOfficerDefaults = [OfficerDefaults.Create(20000)] }, ScenarioContentFiles.DataJsonOptions));
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        workspace.SetOfficer(workspace.GetOfficer(9001) with { ParentId = 30000 });
        var created = await workspace.CreateOfficerAsync();
        Assert.True(created.Id > 30000);
        await workspace.SaveAsync();
    }

    [Fact]
    public async Task NonPlacementRuleReferencesAlsoProtectAuthoredPeople()
    {
        await CreatePeopleAndTactics();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var created = await workspace.CreateOfficerAsync();
        await workspace.SaveAsync();
        var definitions = workspace.Definitions with { OriginalAi = workspace.Definitions.OriginalAi with
        { FamilyRankBonuses = ImmutableDictionary<int, int>.Empty.Add(created.Id, 100) } };
        await File.WriteAllTextAsync(workspace.PlayerDefinitionPath, JsonSerializer.Serialize(definitions, ScenarioContentFiles.DataJsonOptions));
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => reopened.DeleteOfficerAsync(created.Id));
        Assert.Contains("家族排序", failure.Message);
        Assert.True(ContentComparison.Equal(created, reopened.GetOfficer(created.Id)));
    }

    [Fact]
    public async Task ScenarioPlacementBlocksDeletionAndLateReferencesBlockPublication()
    {
        await CreatePeopleAndTactics();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var created = await workspace.CreateOfficerAsync();
        await workspace.SaveAsync();
        byte[] saved = await File.ReadAllBytesAsync(workspace.PlayerOfficerPath);
        string path = Path.Combine(_root, "a", "scenario.json");
        var document = JsonSerializer.Deserialize<ScenarioDocument>(await File.ReadAllTextAsync(path), ScenarioContentFiles.DataJsonOptions)!;
        var placement = document.Officers[0] with { Id = created.Id, Overrides = new() };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document with
        { Officers = [.. document.Officers, placement] }, ScenarioContentFiles.DataJsonOptions));
        var blocked = await Assert.ThrowsAsync<InvalidDataException>(() => workspace.DeleteOfficerAsync(created.Id));
        Assert.Contains("剧本 a", blocked.Message); Assert.Contains("初始状态", blocked.Message);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, ScenarioContentFiles.DataJsonOptions));
        await workspace.DeleteOfficerAsync(created.Id);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document with
        { Officers = [document.Officers[0] with { Overrides = new() { ParentId = new(created.Id) } }] }, ScenarioContentFiles.DataJsonOptions));
        var late = await Assert.ThrowsAsync<InvalidDataException>(() => workspace.SaveAsync());
        Assert.Contains("亲代覆盖", late.Message);
        Assert.Equal(saved, await File.ReadAllBytesAsync(workspace.PlayerOfficerPath));
        Assert.True(workspace.HasChanges);
    }

    [Fact]
    public async Task EditingNamedRulesPreservesUnlistedMilitaryDefinitions()
    {
        await CreatePeopleAndTactics();
        string path = Path.Combine(_root, "shared", "definitions.json");
        var definitions = JsonSerializer.Deserialize<ScenarioDefinitions>(await File.ReadAllTextAsync(path), ScenarioContentFiles.DataJsonOptions)!;
        var hiddenFormation = definitions.Military!.Formations[0] with { Id = 7001 };
        var hiddenTactic = definitions.Military.Tactics[0] with { Id = 6001 };
        definitions = definitions with { Military = definitions.Military with
        { Formations = definitions.Military.Formations.Add(hiddenFormation), Tactics = definitions.Military.Tactics.Add(hiddenTactic) } };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(definitions, ScenarioContentFiles.DataJsonOptions));
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.Equal(7000, Assert.Single(workspace.Formations).Id);
        Assert.Equal(6000, Assert.Single(workspace.Tactics).Id);
        workspace.SetFormation(workspace.GetFormation(7000) with { MovementBudget = 90 });
        workspace.SetTactic(workspace.GetTactic(6000) with { Power = 20 });
        await workspace.SaveAsync();
        var result = (await ScenarioContentFiles.LoadScenarioAsync(Path.Combine(_root, "b"))).Military!;
        Assert.True(ContentComparison.Equal(hiddenFormation, result.Formations.Single(f => f.Id == 7001)));
        Assert.True(ContentComparison.Equal(hiddenTactic, result.Tactics.Single(t => t.Id == 6001)));
    }

    [Fact]
    public async Task CatalogIdentityChangesCheckMessagesBeforePublishingBothPlayerDocuments()
    {
        await CreatePeopleAndTactics();
        Dictionary<string, MessageDefinition> messages = new()
        { ["missing-rank"] = new([new MessageValue(new("property", "rank.name", Arguments: [new("entity", "Rank", 12345)]))]) };
        var library = await MessageLibraryFiles.WriteAsync(_root, "shared/messages/library.json", messages);
        string path = Path.Combine(_root, "a", "content.json");
        var manifest = JsonSerializer.Deserialize<ScenarioContentManifest>(await File.ReadAllTextAsync(path), ScenarioContentFiles.DataJsonOptions)!;
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest with { SharedMessages = library.Path }, ScenarioContentFiles.DataJsonOptions));
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        workspace.SetRank(workspace.GetRank(8000) with { Name = "改名" });
        await workspace.CreateTacticAsync(6000);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => workspace.SaveAsync());
        Assert.Contains("12345", error.Message);
        Assert.False(File.Exists(workspace.PlayerDefinitionPath)); Assert.False(File.Exists(workspace.PlayerOfficerPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalRuleAndDisplayChangesDoNotDependOnWorldGeographyOrMessages(bool displayOnly)
    {
        await Create();
        await File.WriteAllTextAsync(Path.Combine(_root, "a/scenario.json"), "unrelated world");
        await File.WriteAllTextAsync(Path.Combine(_root, "shared/geography.json"), "unrelated geography");
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        if (displayOnly) workspace.SetRank(workspace.GetRank(8000) with { Name = "新名称", Description = "新说明" });
        else workspace.SetTacticProficiencyCap(1500);
        await workspace.SaveAsync();
        Assert.False(workspace.HasChanges);
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.Equal(displayOnly ? 1000 : 1500, reopened.Definitions.OfficerGrowth.TacticProficiencyCap);
        Assert.Equal(displayOnly ? "新名称" : "自制爵位", reopened.GetRank(8000).Name);
        Assert.Equal("unrelated world", await File.ReadAllTextAsync(Path.Combine(_root, "a/scenario.json")));
    }

    [Fact]
    public async Task ReplacingTheSecondPlayerFileRollsBackTheFirstAndKeepsDrafts()
    {
        if (!OperatingSystem.IsWindows()) return;
        await CreatePeopleAndTactics(); var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        workspace.SetRank(workspace.GetRank(8000) with { Name = "第一次" }); await workspace.SaveAsync();
        byte[] definitions = await File.ReadAllBytesAsync(workspace.PlayerDefinitionPath), people = await File.ReadAllBytesAsync(workspace.PlayerOfficerPath);
        workspace.SetRank(workspace.GetRank(8000) with { Name = "第二次" });
        workspace.SetOfficer(workspace.GetOfficer(9001) with { War = 95 });
        using (var locked = new FileStream(workspace.PlayerOfficerPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Record.ExceptionAsync(() => workspace.SaveAsync());
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        }
        Assert.Equal(definitions, await File.ReadAllBytesAsync(workspace.PlayerDefinitionPath));
        Assert.Equal(people, await File.ReadAllBytesAsync(workspace.PlayerOfficerPath));
        Assert.True(workspace.HasChanges);
        Assert.Empty(Directory.GetFiles(_root, "*.pending", SearchOption.AllDirectories));
        await workspace.SaveAsync();
        Assert.Equal("第二次", (await SharedDataEditorWorkspace.OpenAsync(_root)).GetRank(8000).Name);
    }

    [Fact]
    public async Task EveryPagePublishesTogetherAndPersonTextIsProjectedFromTheEffectiveProfile()
    {
        await CreatePeopleAndTactics();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var originals = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        workspace.SetOfficer(workspace.GetOfficer(9001) with { Name = "新姓名", CourtesyName = "新字", Biography = "新列传", PortraitKey = "portrait.edited", War = 95 });
        workspace.SetTactic(workspace.GetTactic(6000) with { Power = 30, MinimumInterval = 9 });
        workspace.SetTacticCatalog(workspace.GetTacticCatalog(6000) with { Name = "新兵法", Description = "新兵法说明" });
        workspace.SetFormationCatalog(workspace.GetFormationCatalog(7000) with { Name = "新阵形", Description = "新阵形说明" });
        workspace.SetFormation(workspace.GetFormation(7000) with { MovementBudget = 90 });
        workspace.SetOffice(workspace.GetOffice(9000) with { Name = "新官职", Description = "新官职说明" });
        workspace.SetRank(workspace.GetRank(8000) with { Name = "新爵位" });
        await workspace.SaveAsync();
        Assert.False(workspace.HasChanges);
        foreach (var (path, bytes) in originals) Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.True(File.Exists(workspace.PlayerDefinitionPath)); Assert.True(File.Exists(workspace.PlayerOfficerPath));
        var content = await ScenarioContentFiles.LoadAsync(Path.Combine(_root, "b"));
        var person = Assert.Single(content.Scenario.Officers);
        Assert.Equal(95, person.State.BaseAbilities.War);
        Assert.Equal("新姓名", content.Text.Resolve(person.NameKey));
        Assert.Equal("portrait.edited", content.Text.MessagePeople[9001].PortraitKey);
        Assert.Equal("portrait.edited", content.IllustrationRoles["catalog-person-9001"]);
        Assert.Equal("新字", content.Text.Resolve(person.CourtesyNameKey!));
        Assert.Equal("新列传", content.Text.ResolveRich(person.BiographyKey!));
        Assert.Equal("新兵法", Assert.Single(content.Scenario.Information.Tactics).Name);
        Assert.Equal(30, content.Scenario.Military!.Tactics.Single().Power);
        Assert.Equal("新阵形", Assert.Single(content.Scenario.Information.Formations).Name);
        var selection = await ScenarioContentFiles.LoadSelectionAsync(Path.Combine(_root, "b"));
        Assert.Equal("新姓名", selection.Text.Resolve(selection.Scenario.Officers.Single().NameKey));
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.Equal("新姓名", reopened.GetOfficer(9001).Name);
        workspace.RestoreFormation(7000);
        Assert.Equal("自制阵形", workspace.GetFormationCatalog(7000).Name);
        Assert.Equal("新兵法", workspace.GetTacticCatalog(6000).Name);
        workspace.RestoreOfficer(9001);
        Assert.Equal("试将", workspace.GetOfficer(9001).Name);
        Assert.Equal(30, workspace.GetTactic(6000).Power);
    }

    [Fact]
    public async Task ExternalPersonCopyPreventsPublishingEitherDocument()
    {
        await CreatePeopleAndTactics(); var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        workspace.SetOfficer(workspace.GetOfficer(9001) with { War = 95 });
        workspace.SetRank(workspace.GetRank(8000) with { Name = "改名" });
        byte[] outside = await File.ReadAllBytesAsync(workspace.OfficerPath);
        await File.WriteAllBytesAsync(workspace.PlayerOfficerPath, outside);
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.SaveAsync());
        Assert.False(File.Exists(workspace.PlayerDefinitionPath));
        Assert.Equal(outside, await File.ReadAllBytesAsync(workspace.PlayerOfficerPath));
        Assert.True(workspace.HasChanges);
    }

    [Fact]
    public async Task RankNamesAndDescriptionsSaveWithTheSharedDraftWithoutWritingTextCatalogs()
    {
        await Create();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var originals = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        workspace.SetRank(workspace.GetRank(8000) with { Name = "新爵位", Description = "新说明\n第二行" });
        await workspace.SaveAsync();
        foreach (var (path, bytes) in originals) Assert.Equal(bytes, File.ReadAllBytes(path));
        foreach (string id in new[] { "a", "b", "private" })
        {
            var rank = Assert.Single((await ScenarioContentFiles.LoadScenarioAsync(Path.Combine(_root, id))).Information.Ranks);
            Assert.Equal("新爵位", rank.Name);
            Assert.Equal("新说明\n第二行", rank.Description);
        }
        Assert.Equal("新爵位", (await SharedDataEditorWorkspace.OpenAsync(_root)).GetRank(8000).Name);
        workspace.RestoreRank(8000);
        Assert.Equal("自制爵位", workspace.GetRank(8000).Name);
        Assert.Equal("", workspace.GetRank(8000).Description);
        await workspace.SaveAsync();
        Assert.Equal("自制爵位", (await SharedDataEditorWorkspace.OpenAsync(_root)).GetRank(8000).Name);
    }

    [Fact]
    public async Task SavingACompletePlayerCopyPreservesBaseAndWorldFilesWhileChangingEffectiveRules()
    {
        await Create();
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.False(workspace.HasChanges); Assert.Equal(3, workspace.ScenarioCount);
        Assert.Equal("自制阵形", workspace.FormationNames[7000]);
        var files = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        workspace.SetFormation(workspace.GetFormation(7000) with { MovementBudget = 90, DeploymentCost = 123, Vision = new(15, 120) });
        Assert.True(workspace.HasChanges);
        Assert.Equal(50, (await ScenarioContentFiles.LoadScenarioAsync(Path.Combine(_root, "b"))).Military!.Formations.Single().MovementBudget);
        await workspace.SaveAsync(); Assert.False(workspace.HasChanges);
        foreach (var (path, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.True(File.Exists(workspace.PlayerDefinitionPath));
        foreach (string id in new[] { "a", "b" })
        {
            var loaded = await ScenarioContentFiles.LoadScenarioAsync(Path.Combine(_root, id));
            Assert.Equal(90, loaded.Military!.Formations.Single().MovementBudget);
            Assert.Equal(123, loaded.Military.Formations.Single().DeploymentCost);
            Assert.Equal(3, loaded.Information.Formations.Single().Presentation!.VisionRadius);
            var selection = await ScenarioContentFiles.LoadSelectionAsync(Path.Combine(_root, id));
            Assert.Equal(120, selection.Scenario.Information.Formations.Single().Presentation!.VisionAngleDegrees);
        }
        Assert.Equal(70, (await ScenarioContentFiles.LoadScenarioAsync(Path.Combine(_root, "private"))).Military!.Formations.Single().MovementBudget);
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.True(reopened.HasPlayerCopy);
        Assert.Equal(90, reopened.GetFormation(7000).MovementBudget);
        workspace.RestoreFormation(7000); Assert.True(workspace.HasChanges); await workspace.SaveAsync();
        Assert.Equal(50, (await SharedDataEditorWorkspace.OpenAsync(_root)).GetFormation(7000).MovementBudget);
    }

    [Fact]
    public async Task PositionEditsUpdatePresentationAndInvalidDataDoesNotReachDisk()
    {
        await Create(); var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        byte[] before = File.ReadAllBytes(workspace.DefinitionPath);
        workspace.SetFormation(workspace.GetFormation(7000) with { MovementBudget = 0 });
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.SaveAsync());
        Assert.True(workspace.HasChanges); Assert.Equal(before, File.ReadAllBytes(workspace.DefinitionPath));
        Assert.False(File.Exists(workspace.PlayerDefinitionPath));
        workspace.RestoreFormation(7000);
        workspace.SetFormation(workspace.GetFormation(7000) with { Positions = [new(2, BattleRank.Front), new(0, BattleRank.Rear)] });
        await workspace.SaveAsync();
        var loaded = await ScenarioContentFiles.LoadScenarioAsync(Path.Combine(_root, "b"));
        var presentation = loaded.Information.Formations.Single().Presentation!;
        Assert.Equal(3, presentation.Columns); Assert.Null(presentation.BackgroundRole); Assert.Null(presentation.AvailableRole);
        Assert.Equal(new[] { new FormationSlot(2, 0), new FormationSlot(0, 2) }, presentation.Slots.ToArray());
        workspace.SetFormation(workspace.GetFormation(7000) with { OfficerCapacity = 3 });
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => workspace.SaveAsync());
        Assert.Contains("阵位数", error.Message);
    }

    [Fact]
    public async Task RevertingFormationGeometryRestoresItsDerivedPresentationWithoutLosingText()
    {
        await Create(); var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        var original = workspace.GetFormation(7000);
        var presentation = workspace.GetFormationCatalog(7000).Presentation;
        workspace.SetFormationCatalog(workspace.GetFormationCatalog(7000) with { Name = "改名" });
        workspace.SetFormation(original with { Positions = [new(3, BattleRank.Front), new(0, BattleRank.Rear)] });
        Assert.Null(workspace.GetFormationCatalog(7000).Presentation!.BackgroundRole);
        workspace.SetFormation(original);
        Assert.True(ContentComparison.Equal(presentation, workspace.GetFormationCatalog(7000).Presentation));
        Assert.Equal("改名", workspace.GetFormationCatalog(7000).Name);
    }

    [Theory]
    [InlineData("shared/definitions.json")]
    [InlineData("shared/definitions.player.json")]
    [InlineData("b/content.json")]
    public async Task ExternalInputChangesArePreservedAndRequireReopening(string relative)
    {
        await Create(); var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        workspace.SetFormation(workspace.GetFormation(7000) with { DeploymentCost = 99 });
        string path = Path.Combine(_root, relative);
        byte[] original = File.ReadAllBytes(File.Exists(path) ? path : workspace.DefinitionPath);
        await File.WriteAllBytesAsync(path, [.. original, (byte)'\n']);
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.SaveAsync());
        Assert.True(workspace.HasChanges); Assert.Equal(original.Length + 1, new FileInfo(path).Length);
        Assert.Empty(Directory.GetFiles(_root, "*.pending", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task SavingUnplacedCatalogAdditionsDoesNotReadGeography()
    {
        await CreatePeopleAndTactics();
        var editor = await SharedDataEditorWorkspace.OpenAsync(_root);
        var officer = await editor.CreateOfficerAsync();
        var treasure = await editor.CreateTreasureAsync();
        await File.WriteAllTextAsync(Path.Combine(_root, "shared/geography.json"), "unavailable");
        await editor.SaveAsync();
        var reopened = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.Equal(officer.Name, reopened.GetOfficer(officer.Id).Name);
        Assert.Equal(treasure.Name, reopened.GetTreasure(treasure.Id).Name);
    }

    [Theory]
    [InlineData("a/scenario.json")]
    [InlineData("shared/geography.json")]
    public async Task OpeningDefersWorldAndGeographyUntilSave(string relative)
    {
        await Create();
        await File.WriteAllTextAsync(Path.Combine(_root, relative), "invalid deferred data");
        var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.Equal(3, workspace.ScenarioCount);
        Assert.Equal(50, workspace.GetFormation(7000).MovementBudget);
        workspace.SetFormation(workspace.GetFormation(7000) with { DeploymentCost = 99 });
        await Assert.ThrowsAsync<JsonException>(() => workspace.SaveAsync());
        Assert.True(workspace.HasChanges);
        Assert.False(File.Exists(workspace.PlayerDefinitionPath));
    }

    [Fact]
    public async Task SavingUsesWorldDataReadAtSaveTime()
    {
        await Create(); var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        string worldPath = Path.Combine(_root, "b", "scenario.json");
        byte[] original = await File.ReadAllBytesAsync(worldPath);
        byte[] updated = [.. original, (byte)'\n'];
        await File.WriteAllBytesAsync(worldPath, updated);
        workspace.SetFormation(workspace.GetFormation(7000) with { DeploymentCost = 99 });
        await workspace.SaveAsync();
        Assert.Equal(updated, await File.ReadAllBytesAsync(worldPath));
        Assert.Equal(99, (await ScenarioContentFiles.LoadScenarioAsync(Path.Combine(_root, "b"))).Military!.Formations.Single().DeploymentCost);
    }

    [Fact]
    public async Task CompletePlayerCopyDoesNotInheritLaterChangesToUneditedBaseFields()
    {
        await Create(); var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        workspace.SetFormation(workspace.GetFormation(7000) with { MovementBudget = 90 }); await workspace.SaveAsync();
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(workspace.DefinitionPath))!;
        document["military"]!["formations"]![0]!["deploymentCost"] = 999;
        await File.WriteAllTextAsync(workspace.DefinitionPath, document.ToJsonString());
        var loaded = await ScenarioContentFiles.LoadScenarioAsync(Path.Combine(_root, "b"));
        Assert.Equal(90, loaded.Military!.Formations.Single().MovementBudget);
        Assert.Equal(12, loaded.Military.Formations.Single().DeploymentCost);
    }

    [Fact]
    public async Task FormationOfficeAndRankDraftsRestoreIndependentlyAndSaveTogether()
    {
        await Create(); var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        Assert.Equal("自制官职", workspace.GetOffice(9000).Name);
        Assert.Equal("自制爵位", workspace.GetRank(8000).Name);
        workspace.SetFormation(workspace.GetFormation(7000) with { MovementBudget = 90 });
        workspace.SetOffice(workspace.GetOffice(9000) with { Salary = 77 });
        workspace.SetRank(workspace.GetRank(8000) with { CommandCapacity = 22000, RequiredTrust = 345, LoyaltyBonus = 12 });
        workspace.RestoreOffice(9000);
        Assert.Equal(90, workspace.GetFormation(7000).MovementBudget);
        Assert.Equal(20, workspace.GetOffice(9000).Salary);
        Assert.Equal(345, workspace.GetRank(8000).RequiredTrust);
        workspace.SetOffice(workspace.GetOffice(9000) with { Salary = 77,
            AbilityBonuses = workspace.GetOffice(9000).AbilityBonuses with { Leadership = 12 } });
        workspace.RestoreFormation(7000);
        Assert.Equal(77, workspace.GetOffice(9000).Salary);
        workspace.SetFormation(workspace.GetFormation(7000) with { DeploymentCost = 199 });
        workspace.RestoreRank(8000);
        Assert.Equal(100, workspace.GetRank(8000).RequiredTrust);
        Assert.Equal(199, workspace.GetFormation(7000).DeploymentCost);
        Assert.Equal(77, workspace.GetOffice(9000).Salary);
        workspace.SetRank(workspace.GetRank(8000) with { CommandCapacity = 22000, RequiredTrust = 345, LoyaltyBonus = 12 });
        await workspace.SaveAsync();
        var loaded = await ScenarioContentFiles.LoadScenarioAsync(Path.Combine(_root, "b"));
        Assert.Equal(199, loaded.Military!.Formations.Single().DeploymentCost);
        Assert.Equal(77, loaded.Information.Offices.Single().Salary);
        Assert.Equal(12, loaded.Information.Offices.Single().AbilityBonuses.Leadership);
        Assert.Equal(22000u, loaded.Information.Ranks.Single().CommandCapacity);
        Assert.Equal(345, loaded.Information.Ranks.Single().RequiredTrust);
        Assert.Equal(12, loaded.Information.Ranks.Single().LoyaltyBonus);
        Assert.False((await SharedDataEditorWorkspace.OpenAsync(_root)).HasChanges);
    }

    [Fact]
    public async Task InvalidOfficePreventsPublishingEveryPageAndKeepsAllDrafts()
    {
        await Create(); var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        byte[] original = await File.ReadAllBytesAsync(workspace.DefinitionPath);
        workspace.SetFormation(workspace.GetFormation(7000) with { DeploymentCost = 199 });
        workspace.SetOffice(workspace.GetOffice(9000) with { Salary = -1 });
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.SaveAsync());
        Assert.False(File.Exists(workspace.PlayerDefinitionPath));
        Assert.Equal(original, await File.ReadAllBytesAsync(workspace.DefinitionPath));
        Assert.Equal(199, workspace.GetFormation(7000).DeploymentCost);
        Assert.Equal(-1, workspace.GetOffice(9000).Salary);
        Assert.True(workspace.HasChanges);
    }

    [Fact]
    public async Task InvalidRankPreventsPublishingOtherPages()
    {
        await Create(); var workspace = await SharedDataEditorWorkspace.OpenAsync(_root);
        workspace.SetFormation(workspace.GetFormation(7000) with { DeploymentCost = 199 });
        workspace.SetOffice(workspace.GetOffice(9000) with { Salary = 77 });
        workspace.SetRank(workspace.GetRank(8000) with { RequiredTrust = -1 });
        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.SaveAsync());
        Assert.False(File.Exists(workspace.PlayerDefinitionPath));
        Assert.Equal(199, workspace.GetFormation(7000).DeploymentCost);
        Assert.Equal(77, workspace.GetOffice(9000).Salary);
        Assert.Equal(-1, workspace.GetRank(8000).RequiredTrust);
        Assert.True(workspace.HasChanges);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
