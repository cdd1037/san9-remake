using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;

namespace San9.Content.Tests;

public sealed class ScenarioCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "san9-composition-" + Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Options = ScenarioContentFiles.DataJsonOptions;

    private static OfficerInitialState Person(int id) => new($"person.{id}", new(id, null, null, new(80, 45, 50, 70), false)
    {
        Status = OfficerStatus.Dead, BirthYear = 150, AppearanceYear = 164,
        ParentId = 123, LearnedTactics = ImmutableSortedSet.Create(1),
        Surname = "姓", TacticProficiencies = [1, 2, 3, 4, 5, 6, 7, 8, 9]
    }) { BiographyKey = "biography", CourtesyNameKey = "courtesy", FlagGlyph = 3 };

    private static ScenarioInitialState Scenario() => new(ScenarioInitialState.CurrentSchemaVersion, "custom", 190, 1, 1,
        null, [], [], [], []) { Officers = [Person(1200)], ImperialSealId = 7 };

    private static ScenarioContent Content(ScenarioInitialState scenario) => new(ScenarioContent.CurrentSchemaVersion, new("../textures"),
        new(new(MapDefinition.CurrentSchemaVersion, "custom", 1, 1, 1, 1, [], [], [], [], [], [], [], [], []), []),
        scenario, Texts(scenario), []);

    private static SharedScenarioData Shared(ScenarioInitialState scenario) => SharedScenarioData.From(scenario,
        "shared/definitions.json", "shared/officer-defaults.json", "shared/geography.json", Texts(scenario));

    private static TextCatalog Texts(ScenarioInitialState scenario) => new(TextCatalog.CurrentSchemaVersion,
        scenario.Officers.SelectMany(p => new[] { p.NameKey, p.CourtesyNameKey, p.BiographyKey }.OfType<string>())
            .Distinct().ToDictionary(k => k, k => k));
    private static ScenarioDocument Describe(SharedScenarioData shared, ScenarioInitialState scenario) => shared.Describe(scenario, Texts(scenario));

    private static void Same<T>(T expected, T actual) => Assert.True(JsonElement.DeepEquals(
        JsonSerializer.SerializeToElement(expected, Options), JsonSerializer.SerializeToElement(actual, Options)));

    [Fact]
    public async Task SharedPortraitEditsAndClearingReachSelectionAndFullContent()
    {
        var baseline = Scenario();
        var shared = Shared(baseline);
        await ScenarioDataFiles.WriteSharedAsync(_root, shared);
        string package = Path.Combine(_root, "portrait");
        await ScenarioContentFiles.WriteAsync(package, Content(baseline), resourceRoot: _root, sharedScenario: shared);
        string path = PlayerDefinitionFiles.PlayerPath(PackageFiles.Resolve(_root, shared.OfficersPath));
        foreach (string? portrait in new string?[] { "custom-portrait", null })
        {
            var edited = shared.Officers with { Officers = [shared.Officers.Officers[0] with { PortraitKey = portrait }] };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(edited, Options));
            var selected = await ScenarioContentFiles.LoadSelectionAsync(package);
            var full = await ScenarioContentFiles.LoadAsync(package);
            Assert.Equal(portrait, selected.Text.MessagePeople[1200].PortraitKey);
            Assert.Equal(portrait, full.Text.MessagePeople[1200].PortraitKey);
            foreach (string role in new[] { "catalog-person-1200", "information-person-1200" })
            {
                Assert.Equal(portrait, selected.IllustrationRoles.GetValueOrDefault(role));
                Assert.Equal(portrait, full.IllustrationRoles.GetValueOrDefault(role));
                Assert.Equal(portrait is not null, full.IllustrationRoles.ContainsKey(role));
            }
        }
    }

    [Fact]
    public async Task AuthoringCatalogReadsSharedPlayerCopiesWithoutScenarioOrWorldDependencies()
    {
        var shared = Shared(Scenario());
        await ScenarioDataFiles.WriteSharedAsync(_root, shared);
        File.Delete(PackageFiles.Resolve(_root, shared.GeographyPath));
        var catalog = new ContentCatalog(ContentCatalog.CurrentSchemaVersion, "missing-textures", "missing-opening.json",
            [new("unavailable", "missing-scenario")])
        { OfficerFlags = "missing-flags.json", Definitions = shared.DefinitionsPath, OfficerDefaults = shared.OfficersPath };
        await File.WriteAllTextAsync(Path.Combine(_root, "catalog.json"), JsonSerializer.Serialize(catalog, PackageFiles.JsonOptions));
        var loaded = await ContentCatalogFiles.LoadAsync(_root);
        var baseline = await ContentCatalogFiles.LoadAuthoringAsync(_root, loaded);
        Assert.Equal(1200, Assert.Single(baseline.Officers.Officers).Id);

        var definitions = shared.Definitions with { Information = shared.Definitions.Information with
        { Tactics = [new(9000, "共享兵法改名")] } };
        var officers = shared.Officers with { Officers = [shared.Officers.Officers[0] with { Name = "共享人物改名" }] };
        string definitionPath = PlayerDefinitionFiles.PlayerPath(PackageFiles.Resolve(_root, loaded.Definitions));
        string officerPath = PlayerDefinitionFiles.PlayerPath(PackageFiles.Resolve(_root, loaded.OfficerDefaults));
        await File.WriteAllTextAsync(definitionPath, JsonSerializer.Serialize(definitions, Options));
        await File.WriteAllTextAsync(officerPath, JsonSerializer.Serialize(officers, Options));
        var player = await ContentCatalogFiles.LoadAuthoringAsync(_root, loaded);
        Assert.Equal("共享兵法改名", Assert.Single(player.Definitions.Information.Tactics).Name);
        Assert.Equal("共享人物改名", Assert.Single(player.Officers.Officers).Name);

        await File.WriteAllTextAsync(officerPath, JsonSerializer.Serialize(officers with { SchemaVersion = 0 }, Options));
        await Assert.ThrowsAsync<InvalidDataException>(() => ContentCatalogFiles.LoadAuthoringAsync(_root, loaded));
    }

    [Fact]
    public void RankTextInheritsSharedDefinitionsUnlessTheScenarioExplicitlyOverridesIt()
    {
        var baseline = Scenario() with { Information = InformationDefinitions.Empty with
        {
            Ranks = [new(8000, "共享名", 10000, 0, 0, 1) { Description = "共享说明" }]
        } };
        var shared = Shared(baseline);
        var inherited = Describe(shared, baseline);
        var local = Describe(shared, baseline with { Information = baseline.Information with
        {
            Ranks = [baseline.Information.Ranks[0] with { Name = "剧本名", Description = "剧本说明" }]
        } });
        var updated = shared.Definitions with { Information = shared.Definitions.Information with
        {
            Ranks = [baseline.Information.Ranks[0] with { Name = "更新共享名", Description = "" }]
        } };
        var inheritedRank = Assert.Single(inherited.Compile(updated, shared.Officers.Officers, shared.Geography).Information.Ranks);
        Assert.Equal("更新共享名", inheritedRank.Name);
        Assert.Equal("", inheritedRank.Description);
        var privateRank = Assert.Single(local.Compile(updated, shared.Officers.Officers, shared.Geography).Information.Ranks);
        Assert.Equal("剧本名", privateRank.Name);
        Assert.Equal("剧本说明", privateRank.Description);
        var serialized = JsonSerializer.SerializeToNode(privateRank, Options)!.AsObject();
        Assert.False(serialized.ContainsKey("nameKey"));
        Assert.False(serialized.ContainsKey("descriptionKey"));
    }

    [Fact]
    public void ScenarioOverridesInheritUntouchedFieldsAndCanExplicitlyClearNullableValues()
    {
        var baseline = Scenario();
        var person = baseline.Officers[0];
        var late = baseline with
        {
            ImperialSealId = null,
            Officers = [person with { BiographyKey = null, State = person.State with
            {
                BaseAbilities = person.State.BaseAbilities with { Intelligence = 55 },
                ParentId = null, LearnedTactics = []
            } }]
        };
        var shared = Shared(baseline);
        var document = Describe(shared, late);
        Same(late, document.Compile(shared.Definitions, shared.Officers.Officers, shared.Geography));
        var json = JsonSerializer.SerializeToNode(document, Options)!;
        var changes = json["officers"]![0]!["overrides"]!.AsObject();
        Assert.False(changes.ContainsKey("war"));
        Assert.Equal(55, changes["intelligence"]!["value"]!.GetValue<int>());
        Assert.Null(changes["parentId"]!["value"]);
        Assert.Empty(changes["learnedTactics"]!["value"]!.AsArray());

        var updatedDefaults = shared.Officers.Officers.Select(p => p with { War = 95, Intelligence = 60 }).ToArray();
        var result = document.Compile(shared.Definitions, updatedDefaults, shared.Geography);
        Assert.Equal(new OfficerAbilities(95, 55, 50, 70), result.Officers[0].State.BaseAbilities);
        Assert.Null(result.Officers[0].State.ParentId);
        Assert.Null(result.ImperialSealId);
        var unchanged = Describe(shared, baseline).Compile(shared.Definitions, updatedDefaults, shared.Geography);
        Assert.Equal(60, unchanged.Officers[0].State.BaseAbilities.Intelligence);
        Assert.Equal(7, unchanged.ImperialSealId);
        Assert.Equal(45, shared.Officers.Officers[0].Intelligence);
    }

    [Fact]
    public void CatalogMembershipDoesNotActivatePeopleAndLocalDefinitionsPreserveStableIdentities()
    {
        var baseline = Scenario();
        var shared = Shared(baseline);
        var different = baseline with { Officers = [Person(9001)] };
        var document = Describe(shared, different);
        Assert.Equal(9001, Assert.Single(document.AdditionalOfficerDefaults).Id);
        var result = document.Compile(shared.Definitions, shared.Officers.Officers, shared.Geography);
        Assert.Equal(9001, Assert.Single(result.Officers).State.Id);
        Assert.Equal(1200, Assert.Single(shared.Officers.Officers).Id);
        Same(different, result);
        Assert.Throws<InvalidDataException>(() => (document with { AdditionalOfficerDefaults = [shared.Officers.Officers[0]] })
            .Compile(shared.Definitions, shared.Officers.Officers, shared.Geography));
        Assert.Throws<InvalidDataException>(() => (document with { AdditionalOfficerDefaults = [] })
            .Compile(shared.Definitions, shared.Officers.Officers, shared.Geography));
        Assert.Throws<InvalidDataException>(() => (document with { Officers = [document.Officers[0], document.Officers[0]] })
            .Compile(shared.Definitions, shared.Officers.Officers, shared.Geography));
    }

    [Fact]
    public void OptionalAppearanceEvidenceSurvivesScenarioComposition()
    {
        var scenario = Scenario() with { AppearanceEvidence = [new(1200, 1200, null, "source", 189)] };
        var shared = Shared(scenario);
        var document = Describe(shared, scenario);
        var restored = document.Compile(shared.Definitions, shared.Officers.Officers, shared.Geography);
        Assert.Equal(scenario.AppearanceEvidence, restored.AppearanceEvidence);
    }

    [Fact]
    public async Task SharedFilesRoundTripThroughBothLoadersWithoutCopyingCatalogsIntoScenarios()
    {
        var baseline = Scenario() with { PersonalPeerageRegimes = [new("han", [new(180, 220,
            ImmutableDictionary<PersonalPeerageGrade, ImmutableArray<int>>.Empty, [])])] };
        var shared = Shared(baseline);
        var person = baseline.Officers[0];
        var second = baseline with { ScenarioId = "late", Year = 220, PersonalPeerageRegimes = [new("wei", [new(220, 280,
            ImmutableDictionary<PersonalPeerageGrade, ImmutableArray<int>>.Empty, [])])], Officers = [person with
        {
            State = person.State with { BaseAbilities = new(80, 55, 50, 70), LearnedTactics = ImmutableSortedSet.Create(1, 2) }
        }] };
        await ScenarioDataFiles.WriteSharedAsync(_root, shared);
        foreach (var scenario in new[] { baseline, second })
        {
            string package = Path.Combine(_root, scenario.ScenarioId);
            await ScenarioContentFiles.WriteAsync(package, Content(scenario), resourceRoot: _root, sharedScenario: shared);
            Same(scenario, await ScenarioContentFiles.LoadScenarioAsync(package));
            Same(scenario, (await ScenarioContentFiles.LoadAsync(package)).Scenario);
            Same(ScenarioSelectionState.From(scenario), (await ScenarioContentFiles.LoadSelectionAsync(package)).Scenario);
            var authored = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(package, "scenario.json")))!.AsObject();
            Assert.False(authored.ContainsKey("military"));
            Assert.False(authored["information"]!.AsObject().ContainsKey("offices"));
            Assert.False(authored["officers"]![0]!.AsObject().ContainsKey("nameKey"));
            Assert.False(File.Exists(Path.Combine(package, "officer-defaults.json")));
        }
        // Missing shared inputs are errors, never an empty catalog or an alternate scenario.
        File.Delete(Path.Combine(_root, shared.OfficersPath));
        await Assert.ThrowsAsync<FileNotFoundException>(() => ScenarioContentFiles.LoadScenarioAsync(Path.Combine(_root, "late")));
        await Assert.ThrowsAsync<FileNotFoundException>(() => ScenarioContentFiles.LoadSelectionAsync(Path.Combine(_root, "late")));
    }

    [Fact]
    public async Task InvalidOverridesAndSharedVersionsFailInsteadOfSilentlyChangingMeaning()
    {
        var scenario = Scenario();
        var shared = Shared(scenario);
        await ScenarioDataFiles.WriteSharedAsync(_root, shared);
        string package = Path.Combine(_root, "scenario");
        await ScenarioContentFiles.WriteAsync(package, Content(scenario), resourceRoot: _root, sharedScenario: shared);
        var document = JsonSerializer.SerializeToNode(Describe(shared, scenario), Options)!;
        document["officers"]![0]!["overrides"]!["inteligence"] = new JsonObject { ["value"] = 60 };
        await File.WriteAllTextAsync(Path.Combine(package, "scenario.json"), document.ToJsonString());
        await Assert.ThrowsAsync<JsonException>(() => ScenarioContentFiles.LoadScenarioAsync(package));
        await Assert.ThrowsAsync<JsonException>(() => ScenarioContentFiles.LoadSelectionAsync(package));
        await ScenarioContentFiles.WriteAsync(package, Content(scenario), resourceRoot: _root, sharedScenario: shared);
        var catalog = JsonSerializer.SerializeToNode(shared.Officers, Options)!;
        catalog["schemaVersion"] = OfficerDefaultsCatalog.CurrentSchemaVersion + 1;
        await File.WriteAllTextAsync(Path.Combine(_root, shared.OfficersPath), catalog.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.LoadScenarioAsync(package));
        await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.LoadSelectionAsync(package));
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData("shared/geography.json")]
    [InlineData("scenario.json")]
    public async Task InvalidSharedPathsCannotPublishAnEntryPoint(string path)
    {
        var scenario = Scenario();
        var shared = Shared(scenario) with { DefinitionsPath = path };
        await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.WriteAsync(_root, Content(scenario), sharedScenario: shared));
        Assert.False(File.Exists(Path.Combine(_root, "content.json")));
        Assert.Empty(Directory.Exists(_root) ? Directory.GetFiles(_root, "*.pending", SearchOption.AllDirectories) : []);
    }

    [Fact]
    public async Task SwitchingToPrivateInputsDoesNotRetireSharedFilesBelowThePackageRoot()
    {
        var scenario = Scenario();
        var shared = Shared(scenario);
        await ScenarioDataFiles.WriteSharedAsync(_root, shared);
        await ScenarioContentFiles.WriteAsync(_root, Content(scenario), sharedScenario: shared);
        string other = Path.Combine(_root, "other");
        await ScenarioContentFiles.WriteAsync(other, Content(scenario), resourceRoot: _root, sharedScenario: shared);
        var sharedFiles = Directory.GetFiles(Path.Combine(_root, "shared"), "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

        await ScenarioContentFiles.WriteAsync(_root, Content(scenario));

        foreach (var (path, bytes) in sharedFiles) Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Same(scenario, await ScenarioContentFiles.LoadScenarioAsync(_root));
        Same(scenario, await ScenarioContentFiles.LoadScenarioAsync(other));
    }

    [Fact]
    public void DifferentGeographyAndUninitializedCollectionsCannotReuseSharedInputs()
    {
        var scenario = Scenario();
        var shared = Shared(scenario);
        Assert.Throws<InvalidDataException>(() => Describe(shared, scenario with { RegionCityIds = new() { [10] = 5 } }));
        var document = Describe(shared, scenario) with { MilitaryPlaces = default };
        Assert.Throws<InvalidDataException>(() => ContentStructure.Validate(document));
        Assert.Throws<InvalidDataException>(() => Describe(shared, scenario with
        { Officers = [scenario.Officers[0] with { State = scenario.Officers[0].State with { TacticProficiencies = default } }] }));
    }

    [Fact]
    public async Task FailedBatchPublicationRestoresSharedDataAndEveryScenario()
    {
        var first = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "first", 190, 1, 1, null, [], [], [], []);
        var second = first with { ScenarioId = "second" };
        var shared = Shared(first);
        await ScenarioDataFiles.WriteSharedAsync(_root, shared);
        foreach (var scenario in new[] { first, second })
            await ScenarioContentFiles.WriteAsync(Path.Combine(_root, scenario.ScenarioId), Content(scenario), resourceRoot: _root, sharedScenario: shared);
        var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        if (!OperatingSystem.IsWindows()) return; // Unix permits replacing an open entry point.
        using (var locked = new FileStream(Path.Combine(_root, "second", "content.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var changed = first with { FoodExchangeRate = 2, Year = 191 };
            var failure = await Record.ExceptionAsync(() => ScenarioContentFiles.WriteWorldsAsync(_root, Shared(changed),
                [("first", changed, Texts(changed)), ("second", changed with { ScenarioId = "second" }, Texts(changed))]));
            Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
        }
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order());
        foreach (var (path, bytes) in before) Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Same(first, await ScenarioContentFiles.LoadScenarioAsync(Path.Combine(_root, "first")));
        Same(second, await ScenarioContentFiles.LoadScenarioAsync(Path.Combine(_root, "second")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
