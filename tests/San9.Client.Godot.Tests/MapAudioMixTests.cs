using San9.Content.Interface;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MapAudioMixTests
{
    [Fact]
    public void ResourceChangeCannotClaimTheOldLoopsAdmissionPriority()
    {
        var playback = new MapAudioPlayback(_ => 0);
        MapSoundSource[] sources =
        [
            new("a", "foot", 500, 100) { Kind = MapSoundKind.Movement },
            new("b", "horse", 501, 100) { Kind = MapSoundKind.Movement }
        ];
        Assert.Equal(2, playback.Update(sources, 1000, 768, []).Length);
        sources[0] = sources[0] with { Role = "horse" };
        Assert.Equal("b", Assert.Single(playback.Update(sources, 1000, 768, [])).Key);
    }

    [Fact]
    public void MovementRejectsOnlyNewIdenticalResourcesInTheSamePanBucket()
    {
        MapSoundSource[] sources = [new("a", "foot", 500, 100) { Kind = MapSoundKind.Movement },
            new("b", "foot", 501, 100) { Kind = MapSoundKind.Movement },
            new("c", "foot", 505, 100) { Kind = MapSoundKind.Movement },
            new("d", "horse", 501, 100) { Kind = MapSoundKind.Movement }];
        Assert.Equal(new[] { "a", "d", "c" }, MapAudioMix.Select(sources, 1000, 768).Select(v => v.Key));
        Assert.Equal(4, MapAudioMix.Select(sources, 1000, 768, new HashSet<string> { "a", "b" }).Length);
        Assert.Equal(4, MapAudioMix.Select(sources.Select(s => s with { Kind = MapSoundKind.Action }), 1000, 768).Length);
    }

    [Fact]
    public void MarchingGainFollowsItsCadenceAndSurvivesVoiceRelease()
    {
        var playback = new MapAudioPlayback(_ => 0);
        MapSoundSource[] sources = [new("army", "foot", 500, 100) { Kind = MapSoundKind.Movement }];
        Assert.Equal(1, Assert.Single(playback.Update(sources, 1000, 768, [], 0)).Gain);
        Assert.Equal(1, Assert.Single(playback.Update(sources, 1000, 768, [], 159)).Gain);
        Assert.Equal(.92, Assert.Single(playback.Update(sources, 1000, 768, [], 160, false)).Gain, 6);
        Assert.Equal(.92, Assert.Single(playback.Update(sources, 1000, 768, [], 319, false)).Gain, 6);
        Assert.Equal(.84, Assert.Single(playback.Update(sources, 1000, 768, [], 320, false)).Gain, 6);
        Assert.Equal(0, Assert.Single(playback.Update(sources, 1000, 768, [], 2080, false)).Gain);
        Assert.Equal(.08, Assert.Single(playback.Update(sources, 1000, 768, [], 2240)).Gain, 6);
        playback.Clear();
        Assert.Equal(.16, Assert.Single(playback.Update(sources, 1000, 768, [], 0)).Gain, 6);
    }

    [Fact]
    public void DisplacementLoopSurvivesVisualTailAndEndsWhenTheUnitStopsOrEntersCombat()
    {
        var sounds = new MapBodySounds(["footsteps"], ["melee"], ["ranged"]);
        Assert.Equal(sounds.Movement, MapUnitAudio.Select(sounds, UnitVisualAction.Moving, true, false));
        Assert.Equal(sounds.Movement, MapUnitAudio.Select(sounds, UnitVisualAction.Idle, true, false));
        Assert.Empty(MapUnitAudio.Select(sounds, UnitVisualAction.Idle, false, false));
        Assert.Empty(MapUnitAudio.Select(sounds, UnitVisualAction.Moving, true, true));
        Assert.Empty(MapUnitAudio.Select(sounds, UnitVisualAction.Idle, true, true));
        Assert.Equal(sounds.Melee, MapUnitAudio.Select(sounds, UnitVisualAction.Melee, true, true));
        Assert.Equal(sounds.Ranged, MapUnitAudio.Select(sounds, UnitVisualAction.Ranged, true, true));
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
        Assert.Empty(MapAudioMix.Select(sources, 0, 100));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DenseColumnsShareThePhysicalResourceLimitAndPreferTheCenter(bool aliasedRoles)
    {
        var sources = Enumerable.Range(0, 100).Select(i => new MapSoundSource($"u:{i}", aliasedRoles ? $"role:{i}" : "foot", i, 50)
            { Resource = aliasedRoles ? "audio/foot.wav" : null });
        var voices = MapAudioMix.Select(sources, 100, 100);
        Assert.Equal(3, voices.Length);
        Assert.Equal("u:50", voices[0].Key);
        Assert.All(voices, v => Assert.InRange(v.X, 49, 51));
        var edge = Assert.Single(MapAudioMix.Select([new("edge", "water", 0, 0)], 100, 100));
        Assert.Equal(0.5, edge.Gain);
        Assert.Empty(MapAudioMix.Select([new("outside", "water", 100, 50)], 100, 100));
    }
}
