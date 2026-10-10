using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Scenarios;

namespace San9.Application.Tests;

public sealed class MapMusicTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(7, 2)]
    [InlineData(10, 3)]
    [InlineData(12, 3)]
    public void RestoredWorldDateAndAuthoredRankReferencesSelectTheMapTrack(int month, int season)
    {
        var audio = new MapAudioDefinition("cue", ["spring", "summer", "autumn", "winter"],
            ["rank-spring", "rank-summer", "rank-autumn", "rank-winter"], [99], [], [], ["battle"]);
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "music", 190, month, 1, 5,
            [new(5, "force", 5, 100)], [new(15, "city", 5, 0, 0, 0, 100, 100, 0, [])], [], [])
        { Information = new([new(99, "rank", 100, 0, 0, 10)], [], [],
            [new San9.Core.Domain.CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], []) };
        var session = GameSession.Start(definition, 1);
        var restored = GameSession.Restore(definition, session.Capture());
        string hash = restored.Snapshot.StateHash;
        Assert.Equal(audio.SeasonalMusic[season], MapMusic.Select(restored.View, audio));
        var ranked = definition with { Forces = [definition.Forces[0] with { RankId = 99 }] };
        var rankedSession = GameSession.Start(ranked, 1);
        Assert.Equal(audio.RankedSeasonalMusic[season], MapMusic.Select(rankedSession.View, audio));
        Assert.Equal(hash, restored.Snapshot.StateHash);
    }
}
