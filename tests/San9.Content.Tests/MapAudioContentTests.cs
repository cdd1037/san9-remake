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
            ["spring", "summer", "autumn", "winter"], [99], new() { ["custom"] = new(["foot"], [], []) },
            new() { [1000] = "build" }, ["battle"]) { RecordNotificationSound = "notification" };
        var sounds = audio.SoundRoles.Distinct().ToDictionary(r => r, r => $"audio/{r}.wav");
        audio.Validate(sounds);
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
