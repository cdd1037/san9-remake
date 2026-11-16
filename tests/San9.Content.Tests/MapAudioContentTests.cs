using San9.Content.Interface;
using San9.Content.Maps;
using San9.Content.Text;

namespace San9.Content.Tests;

public sealed class MapAudioContentTests
{
    [Fact]
    public void MapAudioIsValidatedWithoutProgressPresentation()
    {
        var presentation = CommandPresentation.Empty with
        {
            MapAudio = new("missing", ["spring", "summer", "autumn", "winter"],
                ["spring", "summer", "autumn", "winter"], [], [], [], ["battle"])
        };
        Assert.Throws<InvalidDataException>(() => presentation.Validate(
            new TextCatalog(TextCatalog.CurrentSchemaVersion, []), [], []));
    }

    [Fact]
    public void AuthoredAudioRejectsMissingSoundsAndUnresolvedBodiesOrRanks()
    {
        var audio = new MapAudioDefinition("cue", ["spring", "summer", "autumn", "winter"],
            ["spring", "summer", "autumn", "winter"], [99], new() { ["custom"] = new(["foot"], [], [])
                { Retirement = new(["defeat"], 400, "fade"), BuildingMelee = ["impact"], Engagement = new(["accent"], 1000, 15) } },
            new() { [1000] = "build" }, ["battle"]) { ConstructionCompleteSound = "complete", RecordNotificationSound = "notification", ExecutionMusic = new("combat", "rank-combat", "march", "rank-march", "domestic") };
        var sounds = audio.SoundRoles.Distinct().ToDictionary(r => r, r => $"audio/{r}.wav");
        audio.Validate(sounds);
        Assert.Throws<InvalidDataException>(() => audio.Validate(sounds.Where(s => s.Key != "impact").ToDictionary()));
        Assert.Throws<InvalidDataException>(() => audio.Validate(sounds.Where(s => s.Key != "accent").ToDictionary()));
        Assert.Throws<InvalidDataException>(() => (audio with { BuildingDamageSounds = new() { [1000] = "missing" } }).Validate(sounds));
        Assert.Throws<InvalidDataException>(() => (audio with { Bodies = new() { ["custom"] = new([], [], [])
            { Engagement = new(["accent"], 0, 15) } } }).Validate(sounds));
        Assert.Throws<InvalidDataException>(() => audio.Validate(sounds.Where(s => s.Key != "complete").ToDictionary()));
        var node = System.Text.Json.JsonSerializer.SerializeToNode(audio, San9.Content.Packages.PackageFiles.JsonOptions)!.AsObject();
        node.Remove("constructionCompleteSound");
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<MapAudioDefinition>(node, San9.Content.Packages.PackageFiles.JsonOptions));
        Assert.Throws<InvalidDataException>(() => audio.Validate(sounds.Where(s => s.Key != "fade").ToDictionary()));
        Assert.Throws<InvalidDataException>(() => (audio with { Bodies = new() { ["custom"] = new([], [], [])
            { Retirement = new(["defeat"], -1, null) } } }).Validate(sounds));
        Assert.Throws<InvalidDataException>(() => (audio with { ExecutionMusic = null! }).Validate(sounds));
        var missingMusic = new Dictionary<string, string>(sounds);
        missingMusic.Remove("rank-march");
        Assert.Throws<InvalidDataException>(() => audio.Validate(missingMusic));
        sounds.Remove("foot");
        Assert.Throws<InvalidDataException>(() => audio.Validate(sounds));
        var units = MapUnitCatalog.Empty with { Bodies = [new("custom", [], 0, 0)] };
        audio.ValidateReferences(units, [99], [1000]);
        Assert.Throws<InvalidDataException>(() => audio.ValidateReferences(units, [1], [1000]));
        Assert.Throws<InvalidDataException>(() => audio.ValidateReferences(MapUnitCatalog.Empty, [99], [1000]));
        Assert.Throws<InvalidDataException>(() => audio.ValidateReferences(units, [99], [7]));
        Assert.Throws<InvalidDataException>(() => (audio with { SeasonalMusic = ["spring"] }).Validate(sounds));
    }
}
