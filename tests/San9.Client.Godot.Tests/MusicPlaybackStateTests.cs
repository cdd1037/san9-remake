namespace San9.Client.Godot.Tests;

public sealed class MusicPlaybackStateTests
{
    [Fact]
    public void FadeRetainsTheSceneUntilSilenceThenRestoresItsLiveOwnerOnce()
    {
        var state = new MusicPlaybackState();
        state.SetMapTrack("spring");
        var map = state.Audible;
        var scene = state.Begin("ending", true);
        int completed = 0;
        scene.FadeOut(2, () => completed++);
        state.Advance(.5);
        Assert.Equal("ending", state.Audible!.Resource);
        Assert.Equal(.75f, state.SceneGain);
        state.SetEnabled(false);
        state.Advance(.5);
        state.SetEnabled(true);
        Assert.Equal(.5f, state.SceneGain);
        state.Advance(1);
        Assert.Same(map, state.Audible);
        Assert.Equal(1, completed);
        state.Advance(10); scene.Dispose();
        Assert.Equal(1, completed);
        Assert.Single(state.Tracks);
    }

    [Fact]
    public void DisposingTheSceneCancelsItsPendingFadeCompletion()
    {
        var state = new MusicPlaybackState();
        var scene = state.Begin("ending", true);
        scene.FadeOut(2, () => Assert.Fail("Closed scene must not resume its presentation."));
        state.Advance(1);
        scene.Dispose();
        state.Advance(2);
        Assert.Null(state.Audible);
        Assert.Empty(state.Tracks);
    }

    [Fact]
    public void FadingOneLeaseDoesNotAttenuateOrReleaseAnotherOwnerOfTheSameStream()
    {
        var state = new MusicPlaybackState();
        var outer = state.Begin("ending", true);
        var track = state.Audible;
        outer.FadeOut(2, () => { });
        state.Advance(1);
        using var inner = state.Begin("ending", true);
        Assert.Same(track, state.Audible);
        Assert.Equal(1, state.SceneGain);
        state.Advance(1);
        Assert.Same(track, state.Audible);
        Assert.Equal(1, state.SceneGain);
        Assert.Single(state.Tracks);
    }

    [Fact]
    public void SceneReleaseRestoresTheSameLiveMapPlayback()
    {
        var state = new MusicPlaybackState();
        state.SetMapTrack("spring");
        var map = state.Audible;
        using (state.Begin("diplomacy", loop: true))
        {
            Assert.Equal("diplomacy", state.Audible!.Resource);
            state.SetMapTrack("spring");
            Assert.Contains(map!, state.Tracks);
        }
        Assert.Same(map, state.Audible);
        Assert.Single(state.Tracks);
    }

    [Fact]
    public void MutingKeepsOwnershipAndAcceptsSceneAndMapChanges()
    {
        var state = new MusicPlaybackState();
        state.SetMapTrack("spring");
        var scene = state.Begin("diplomacy", loop: true);
        var music = state.Audible;
        state.SetEnabled(false);
        Assert.Null(state.Audible);
        state.SetMapTrack("summer");
        state.SetEnabled(true);
        Assert.Same(music, state.Audible);
        state.SetEnabled(false);
        scene.Dispose();
        using (state.Begin("elimination", loop: false))
        {
            Assert.Null(state.Audible);
            state.SetEnabled(true);
            Assert.Equal("elimination", state.Audible!.Resource);
            Assert.False(state.Audible.Loop);
        }
        Assert.Equal("summer", state.Audible!.Resource);
    }

    [Fact]
    public void ReleasingAnObscuredSceneNeverResurrectsIt()
    {
        var state = new MusicPlaybackState();
        state.SetMapTrack("spring");
        var outer = state.Begin("diplomacy", loop: true);
        var inner = state.Begin("elimination", loop: false);
        var current = state.Audible;
        outer.Dispose();
        Assert.Same(current, state.Audible);
        inner.Dispose(); inner.Dispose();
        Assert.Equal("spring", state.Audible!.Resource);
        Assert.Single(state.Tracks);
    }

    [Fact]
    public void SamePhysicalMusicAndLoopModeRetainPlaybackAcrossOwners()
    {
        var state = new MusicPlaybackState();
        state.SetMapTrack("shared.ogg");
        var map = state.Audible;
        using (state.Begin("shared.ogg", loop: true)) Assert.Same(map, state.Audible);
        using (state.Begin("shared.ogg", loop: false))
        {
            Assert.NotSame(map, state.Audible);
            Assert.False(state.Audible!.Loop);
        }
        Assert.Same(map, state.Audible);
        state.SetMapTrack(null);
        Assert.Null(state.Audible);
        Assert.Empty(state.Tracks);
    }

    [Fact]
    public void ANewMapWaitsUntilAllLiveScenesRelease()
    {
        var state = new MusicPlaybackState();
        state.SetMapTrack("spring");
        using (state.Begin("diplomacy", loop: true))
        {
            var outer = state.Audible;
            using (state.Begin("elimination", loop: false)) state.SetMapTrack("battle");
            Assert.Same(outer, state.Audible);
        }
        Assert.Equal("battle", state.Audible!.Resource);
        Assert.True(state.Audible.Loop);
    }
}
