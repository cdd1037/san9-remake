namespace San9.Client.Godot.Tests;

public sealed class MapAudioMixTests
{
    [Fact]
    public void DifferentRolesSharingOneAssetShareTheVoiceLimit()
    {
        var sources = Enumerable.Range(0, 8).Select(i =>
            new MapSoundSource($"unit:{i}", $"body:{i}", 50 + i, 50) { Resource = "audio/foot.wav" });
        Assert.Equal(new[] { "unit:0", "unit:1", "unit:2" },
            MapAudioMix.Select(sources, 100, 100).Select(v => v.Key));
    }

    [Fact]
    public void GlobalVoiceLimitKeepsNearerSourcesAcrossResourceGroups()
    {
        var sources = Enumerable.Range(0, 24).Select(i => new MapSoundSource($"near:{i}", $"sound:{i}", 50 + i, 50))
            .Append(new("far", "sound:0", 0, 50));
        var voices = MapAudioMix.Select(sources, 100, 100);
        Assert.Equal(24, voices.Length);
        Assert.DoesNotContain(voices, v => v.Key == "far");
        Assert.Contains(voices, v => v.Key == "near:23");
    }

    [Fact]
    public void RepaintPauseAndCameraReentryCannotRepeatAnEmittedPulseButTheNextDayCan()
    {
        var history = new MapSoundPulses();
        object day = new(); history.Begin(day);
        var shot = new MapSoundVoice("arrow", "shot", 50, 50, 1) { Pulse = 0 };
        Assert.False(history.WasEmitted(shot));
        history.MarkEmitted(shot);
        history.Begin(day);
        Assert.True(history.WasEmitted(shot));
        Assert.True(history.WasEmitted(shot with { X = 70 }));
        Assert.False(history.WasEmitted(shot with { Pulse = 1 }));
        Assert.False(history.WasEmitted(shot with { Pulse = null }));
        history.Begin(new object());
        Assert.False(history.WasEmitted(shot));
    }
    [Fact]
    public void BattleBedIsSelectedOnceAndSuppressesConstructionUntilTheBattleLeavesView()
    {
        int selections = 0;
        var playback = new MapAudioPlayback(_ => { selections++; return 1; });
        MapSoundSource[] sources = [new("build", "hammer", 50, 50) { Kind = MapSoundKind.Construction },
            new("enemy", "battle", 40, 50) { Kind = MapSoundKind.BattleAmbience },
            new("ally", "battle", 60, 50) { Kind = MapSoundKind.BattleAmbience }];
        var bed = Assert.Single(playback.Update(sources, 100, 100, ["battle-a", "battle-b"]));
        Assert.Equal("battle-b", bed.Role);
        Assert.Equal(bed, Assert.Single(playback.Update(sources.Skip(1), 100, 100, ["battle-a", "battle-b"])));
        Assert.Equal(1, selections);
        Assert.Equal("build", Assert.Single(playback.Update(sources.Take(1), 100, 100, ["battle-a", "battle-b"])).Key);
        playback.Update(sources, 100, 100, ["battle-a", "battle-b"]);
        Assert.Equal(2, selections);
        playback.Clear();
        playback.Update(sources, 100, 100, ["battle-a", "battle-b"]);
        Assert.Equal(3, selections);
    }
    [Fact]
    public void CameraMovementDropsDepartedVoicesAndRetainsStableIdentities()
    {
        MapSoundSource[] sources = [new("march", "foot", 50, 50), new("build", "hammer", 150, 50)];
        var initial = Assert.Single(MapAudioMix.Select(sources, 100, 100));
        Assert.Equal("march", initial.Key);
        Assert.Equal(1, initial.Gain);
        var moved = Assert.Single(MapAudioMix.Select(sources.Select(s => s with { X = s.X - 100 }), 100, 100));
        Assert.Equal("build", moved.Key);
        Assert.Equal(moved, Assert.Single(MapAudioMix.Select(sources.Select(s => s with { X = s.X - 100 }), 100, 100)));
        Assert.Empty(MapAudioMix.Select(sources, 0, 100));
    }

    [Fact]
    public void DenseColumnsDoNotOverloadTheMixerAndCenterVoicesWin()
    {
        var sources = Enumerable.Range(0, 100).Select(i => new MapSoundSource($"u:{i}", "foot", i, 50));
        var voices = MapAudioMix.Select(sources, 100, 100);
        Assert.Equal(3, voices.Length);
        Assert.Equal("u:50", voices[0].Key);
        Assert.All(voices, v => Assert.InRange(v.X, 49, 51));
        var edge = Assert.Single(MapAudioMix.Select([new("edge", "water", 0, 0)], 100, 100));
        Assert.Equal(0.5, edge.Gain);
        Assert.Empty(MapAudioMix.Select([new("outside", "water", 100, 50)], 100, 100));
    }
}
