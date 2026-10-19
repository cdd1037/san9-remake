namespace San9.Client.Godot.Tests;

public sealed class MusicPlaybackStateTests
{
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
