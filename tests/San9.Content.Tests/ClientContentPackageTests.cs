using San9.Content.Images;
using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Content.Interface;
using San9.Content.Packages;
using San9.Content.Text;

namespace San9.Content.Tests;

public sealed class ClientContentPackageTests
{
    [Fact]
    public void ScenarioRejectsDanglingLeaderAndCorpsWithTwoRootOwners()
    {
        var initial = Fixture();
        Assert.Throws<InvalidDataException>(() => (initial with
            { Forces = [initial.Forces[0] with { LeaderId = 99 }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (initial with
            { Settlements = [initial.Settlements[0], initial.Settlements[1] with { CorpsId = 5 }] }).Validate());
        var twoForces = initial with { Forces = [initial.Forces[0], initial.Forces[0] with { Id = 6 }],
            Settlements = [initial.Settlements[0] with { CorpsId = 5 }, initial.Settlements[1] with { OwnerId = 6, CorpsId = 5 }] };
        Assert.Throws<InvalidDataException>(twoForces.Validate);
    }
    [Fact]
    public void SquareDisplayContractRequiresRegionVisibilityFlag()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<MapSquare>(
            "{\"Terrain\":0,\"Elevation\":0,\"ElevationMask\":0,\"RegionId\":15}"));
    }
    private static Dictionary<string, string> Colors() => Enumerable.Range(0, 52).Select(i => $"city-marker-{i}")
        .Concat(Enumerable.Range(0, 51).Select(i => $"overview-force-{i}"))
        .Concat(["roster-available", "roster-unavailable", "map-record-normal", "map-record-hover", "map-record-pressed", "map-speech-text", "map-speech-name-shadow"])
        .ToDictionary(s => s, _ => "F0F0F0");
    private static readonly MapBuildingPlacement[] Buildings = [new(15, 89, 64, 0), new(100, 86, 65, 1)];
    private static readonly ImageAsset Asset = new("ui", "ui/frame.png", 32, 32);
    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion, "fixture", 190, 1, 1, 5,
        [new(5, "player", 2, 100)],
        [new(15, "city", 5, 89, 64, 32000, 500, 1000, 50, []), new(100, "port", null, 86, 65, 1000, 200, 300, 50, []) { Kind = San9.Core.Domain.SettlementKind.Facility }],
        new() { [15] = "city" }, []) { Information = new([], [], [], [new San9.Core.Domain.CorpsState(5, 5, null, new(9520, 73200)) { IsRoot = true }], []) };

    [Fact]
    public void ScenarioValidatesOwnershipWithoutMapOrSkin()
    {
        var p = Fixture(); p.Validate();

        Assert.Throws<InvalidDataException>(() => (p with { Settlements = [p.Settlements[0], p.Settlements[0]] }).Validate());
        Assert.Throws<InvalidDataException>(() => (p with { Settlements = [p.Settlements[0] with { OwnerId = 99 }, p.Settlements[1]] }).Validate());

        Assert.Throws<InvalidDataException>(() => (p with { PlayerForceId = 6 }).Validate());
        Assert.Throws<InvalidDataException>(() => (p with { Forces = [p.Forces[0], p.Forces[0]] }).Validate());
    }
    [Fact]
    public void MissingUiAndInvalidDurabilityOrNamesFailValidation()
    {
        var skin = new InterfaceSkin(InterfaceSkin.CurrentSchemaVersion, [Asset], InterfaceSkin.RequiredUiAssets.ToDictionary(s => s, _ => "ui"), Colors(), CursorDefinition.EdgeScrollRoles.ToDictionary(s => s, _ => new CursorDefinition("ui", 0, 0)));
        skin.Validate(); skin.Roles.Remove("top-frame");
        Assert.Throws<InvalidDataException>(skin.Validate);
        skin.Roles["top-frame"] = "missing";
        Assert.Throws<InvalidDataException>(skin.Validate);
        var p = Fixture();
        Assert.Throws<InvalidDataException>(() => (p with { Settlements = [p.Settlements[0] with { Durability = 1001 }, p.Settlements[1]] }).Validate());
        Assert.Throws<InvalidDataException>(() => (p with { Settlements = [p.Settlements[0] with { NameKey = "" }, p.Settlements[1]] }).Validate());
    }
    [Fact]
    public void DisplaySnapshotRejectsInventedIconsMoraleAndRegionReferences()
    {
        var snapshot = Fixture();
        void Reject(SettlementInitialState city) => Assert.Throws<InvalidDataException>(() =>
            (snapshot with { Settlements = [city, snapshot.Settlements[1]] }).Validate());
        Reject(snapshot.Settlements[0] with { Morale = 101 });
        Reject(snapshot.Settlements[0] with { Soldiers = 0 });
        Reject(snapshot.Settlements[0] with { Icons = ["merchant", "training"] });
        Reject(snapshot.Settlements[0] with { Commerce = 1, MaxCommerce = 0 });
        Assert.Throws<InvalidDataException>(() => (snapshot with { RegionCityIds = new() { [15] = 99 } }).Validate());
        Assert.Throws<InvalidDataException>(() => (snapshot with { Settlements = [snapshot.Settlements[0], snapshot.Settlements[1] with { Icons = ["emperor"] }] }).Validate());
    }
    [Fact]
    public void CompositionChecksIdentityCoordinatesAndEveryTextReference()
    {
        var map = new MapPackage(15, "fixture", "fixture", "spring", 200, 200, 13056, 6656,
            [], [], [], [], [], [], Buildings, [], [], [], [], [], new([], []), []);
        var skin = new InterfaceSkin(InterfaceSkin.CurrentSchemaVersion, [Asset], InterfaceSkin.RequiredUiAssets.ToDictionary(s => s, _ => "ui"), Colors(), CursorDefinition.EdgeScrollRoles.ToDictionary(s => s, _ => new CursorDefinition("ui", 0, 0)));
        var text = new TextCatalog(San9.Content.Text.TextCatalog.CurrentSchemaVersion, new() { ["player"] = "Player", ["city"] = "City", ["port"] = "Port", [TextCatalog.ScenarioTitleKey] = "Scenario" });
        foreach (var (key, value) in San9.Tests.CommandTextFixture.Entries()) text.Entries.Add(key, value);
        text.Entries.Add(CommandMenuSkin.OfficerSelectionHintKey, "Select officers");
        foreach (string id in CommandMenuSkin.ReasonIds) text.Entries.Add(CommandMenuSkin.ReasonKey(id), "Reason");
        foreach (string id in San9.Content.Interface.CommandMenuSkin.EntryIds)
        {
            text.Entries.Add(San9.Content.Interface.CommandMenuSkin.DescriptionKey(id), "Description");
            text.Entries.Add(San9.Content.Interface.CommandMenuSkin.HintKey(id), "Hint");
        }
        foreach (string role in StrategyHintText.Roles) text.Entries.Add(StrategyHintText.Key(role), "Hint");
        foreach (string role in SpecialDeploymentText.Roles) text.Entries.Add(SpecialDeploymentText.Key(role), "Hint");
        foreach (string column in ExecutionPanelSkin.Columns) text.Entries.Add(ExecutionPanelSkin.HeaderKey(column), "Header");
        var p = new ClientContentPackage(ScenarioContent.CurrentSchemaVersion, [], map, Fixture(), text, skin, new(3, [])); p.ValidateLinks();
        Assert.Throws<InvalidDataException>(() => (p with { SchemaVersion = 15 }).ValidateLinks());
        Assert.Throws<InvalidDataException>(() => (p with { Scenario = p.Scenario with { Settlements = [p.Scenario.Settlements[0]] } }).ValidateLinks());
        Assert.Throws<InvalidDataException>(() => (p with { Scenario = p.Scenario with { ScenarioId = "other" } }).ValidateLinks());
        Assert.Throws<InvalidDataException>(() => (p with { Scenario = p.Scenario with { Settlements = [p.Scenario.Settlements[0] with { X = 88 }, p.Scenario.Settlements[1]] } }).ValidateLinks());
        foreach (string key in text.Entries.Keys)
        {
            var entries = new Dictionary<string, string>(text.Entries); entries.Remove(key);
            Assert.Throws<InvalidDataException>(() => (p with { Text = new(San9.Content.Text.TextCatalog.CurrentSchemaVersion, entries) }).ValidateLinks());
        }
        var titled = p with { Scenario = p.Scenario with { Information = p.Scenario.Information with
        { CountryTitles = [new(new(5000, true, true, false, [], []), "country-title", 30)] } }, Text = new(San9.Content.Text.TextCatalog.CurrentSchemaVersion, new(text.Entries)) };
        Assert.Throws<InvalidDataException>(titled.ValidateLinks);
        titled.Text.Entries.Add("country-title", "国号");
        titled.ValidateLinks(); // Unclaimed definitions must also retain a valid display name.
        var translated = text with { Entries = text.Entries.ToDictionary(kv => kv.Key, kv => "中文") };
        foreach (var (role, count) in CommandText.Parameters)
            translated.Entries[CommandText.Key(role)] = "中文" + string.Concat(Enumerable.Range(0, count).Select(i => " {" + i + "}"));
        var changed = p with { Text = translated }; changed.ValidateLinks();
        var treasurePackage = p with { Scenario = p.Scenario with
        {
            Information = new([], [], [new(0, "treasure", 0, 0, 0, null)], p.Scenario.Information.Corps, [new(0, null, San9.Core.Domain.TreasureDiscovery.Discovered, 15)])
        }, Text = new(San9.Content.Text.TextCatalog.CurrentSchemaVersion, new(text.Entries)), Skin = skin with { Roles = new(skin.Roles) } };
        treasurePackage.Skin.Roles.Add("information-treasure-0", "ui");
        treasurePackage.Text.Entries.Add("treasure", "Treasure");
        Assert.Throws<InvalidDataException>(treasurePackage.ValidateLinks);
        treasurePackage.Text.Entries.Add(TreasureText.DescriptionKey(0), "First\nSecond");
        treasurePackage.ValidateLinks();
        Assert.Throws<InvalidDataException>(() => (treasurePackage.Scenario with
        {
            Information = treasurePackage.Scenario.Information with
            { TreasureStates = [new(0, 999, San9.Core.Domain.TreasureDiscovery.Discovered, 15)] }
        }).Validate());
        Assert.Throws<InvalidDataException>(() => (treasurePackage.Scenario with
        {
            Information = treasurePackage.Scenario.Information with
            { TreasureStates = [new(0, null, San9.Core.Domain.TreasureDiscovery.Buried, 999)] }
        }).Validate());
        Assert.Same(p.Map, changed.Map); Assert.Same(p.Scenario, changed.Scenario);
        Assert.Equal("中文", changed.Text.Resolve("city")); Assert.Equal("City", text.Entries["city"]);
    }
}
