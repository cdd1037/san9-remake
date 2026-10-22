using San9.Content.Maps;
using San9.Core.Domain;
using San9.Rendering;
using static San9.Client.Godot.Tests.UnitVisualFixture;

namespace San9.Client.Godot.Tests;

public sealed class UnitMotionPlaybackTests
{
    [Fact]
    public void NonblockingBodyActionSurvivesDayPublicationAndKeepsItsOwnStartupClock()
    {
        var playback = new UnitMotionPlayback();
        Advance(playback, 1000);
        playback.PlayAttack(new("first", UnitVisualAction.Melee, 3, 1000, 1200, 200));
        var next = playback.Copy();
        next.Begin(State, Catalog, 1000, 1000, false);
        next.Advance(1100, 1100, true, _ => State, Catalog, Clips, t => t);
        Assert.Null(UnitVisuals.Resolve(State, Catalog, Clips, next).AttackMilliseconds);
        next.Advance(1500, 1500, true, _ => State, Catalog, Clips, t => t);
        var body = UnitVisuals.Resolve(State, Catalog, Clips, next);
        Assert.Equal(UnitVisualAction.Melee, body.Action);
        Assert.Equal(3, body.Direction);
        Assert.Equal(300, body.AttackMilliseconds);
        Assert.Equal("first", body.AttackId);
        next.PlayAttack(new("first", UnitVisualAction.Melee, 3, 1500, 1200, 200));
        Assert.Equal(300, UnitVisuals.Resolve(State, Catalog, Clips, next).AttackMilliseconds);
        next.Advance(2200, 2200, true, _ => State, Catalog, Clips, t => t);
        Assert.Null(UnitVisuals.Resolve(State, Catalog, Clips, next).AttackMilliseconds);
        Assert.Equal(1000, playback.VisualTime);
    }
    private static UnitVisualState March(double time) => State with
    {
        Soldiers = 1001, IsMoving = time < 1000,
        Action = time < 1000 ? UnitVisualAction.Moving : UnitVisualAction.Idle,
        CellX = State.CellX + MapMovementCadence.Progress(time)
    };

    private static void Advance(UnitMotionPlayback playback, double time,
        Func<double, UnitVisualState>? history = null, MapUnitCatalog? catalog = null,
        double? visualTime = null, Func<double, double>? emission = null)
    {
        history ??= March;
        catalog ??= TrailCatalog;
        if (!playback.IsInitialized) playback.Begin(history(-100), catalog, 0, 0, history(0).IsMoving);
        playback.Advance(time, visualTime ?? time, true, history, catalog, Clips, emission ?? (t => t));
    }

    [Fact]
    public void AttackKeepsTheLastPositionCallbackButStopsMovementTrails()
    {
        UnitVisualState History(double t) => March(t) with
            { Soldiers = 1000, Action = t < 900 ? UnitVisualAction.Moving : UnitVisualAction.Melee };
        var playback = new UnitMotionPlayback();
        var leader = Catalog.Formations[0].Layouts[0].Members[0];
        Advance(playback, 899, History);
        var before = playback.Position(leader);
        Advance(playback, 900, History);
        Assert.Equal(before.X + 3.2, playback.Position(leader).X, 6);
        Assert.DoesNotContain(playback.Trails, trail => trail.Emitted >= 900);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public void SamplingFrequencyDoesNotChangePlaybackIncludingPauseAndResume(int pause)
    {
        double Emission(double t) => t < 200 ? t : t + pause;
        var frequent = new UnitMotionPlayback();
        for (int visual = 0; visual < 1050 + pause; visual += 7)
            Advance(frequent, visual < 200 ? visual : Math.Max(200, visual - pause),
                visualTime: visual, emission: Emission);
        Advance(frequent, 1050, visualTime: 1050 + pause, emission: Emission);
        var single = new UnitMotionPlayback();
        Advance(single, 1050, visualTime: 1050 + pause, emission: Emission);
        Assert.NotEmpty(single.Trails);
        Assert.Equal(UnitVisuals.Resolve(March(1050), TrailCatalog, Clips, single).Animations,
            UnitVisuals.Resolve(March(1050), TrailCatalog, Clips, frequent).Animations);
    }

    [Fact]
    public void PauseFreezesPositionsWhileBodyFramesAdvanceAndTrailsExpire()
    {
        var playback = new UnitMotionPlayback();
        var members = Catalog.Formations[0].Layouts[1].Members;
        Advance(playback, 180);
        var positions = members.Select(playback.Position).ToArray();
        int frame = playback.Frame(members[0], 2);
        Assert.NotEmpty(playback.Trails);
        Advance(playback, 180, visualTime: 280);
        Assert.Equal(positions, members.Select(playback.Position));
        Assert.NotEqual(frame, playback.Frame(members[0], 2));
        Advance(playback, 180, visualTime: 1180);
        Assert.Equal(positions, members.Select(playback.Position));
        Assert.Empty(playback.Trails);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoppedFollowersCatchUpOnTheirTimerOrTheNextMapCallback(bool mapAdvances)
    {
        UnitVisualState History(double t) => March(t) with
            { ReceivesPositionUpdates = mapAdvances, Morale = t < 2000 ? 0 : 100 };
        var playback = new UnitMotionPlayback();
        var follower = Catalog.Formations[0].Layouts[1].Members[1];
        Advance(playback, 999, History);
        var prior = playback.Position(follower);
        var target = UnitVisuals.Resolve(History(2500), Catalog, Clips).Animations.Last(a => a.IsBody);
        Assert.True(prior.X < target.GroundX);
        double tick = mapAdvances ? 1000 : Math.Ceiling(1000 / playback.Interval(follower)) * playback.Interval(follower);
        Advance(playback, tick - 1, History);
        Assert.Equal(prior, playback.Position(follower));
        Advance(playback, tick, History);
        Assert.Equal(prior.X + 3.2, playback.Position(follower).X, 6);
        Assert.Equal(prior.Y + 1.6, playback.Position(follower).Y, 6);
        Advance(playback, 2500, History);
        Assert.Equal((target.GroundX, target.GroundY), playback.Position(follower));
    }

    [Fact]
    public void NextDayRetainsQueuesTimersAndTrailsWithoutMutatingThePreviousSnapshot()
    {
        var catalog = OverlappingCatalog with { Bodies = TrailCatalog.Bodies };
        var playback = new UnitMotionPlayback();
        var follower = catalog.Formations[0].Layouts[1].Members[1];
        Advance(playback, 999, catalog: catalog);
        var point = playback.Position(follower);
        int frame = playback.Frame(follower, 2);
        var trails = playback.Trails.ToArray();
        var next = playback.Copy();
        next.Begin(March(999), catalog, 1000, 1000, true);
        Assert.Equal(point, next.Position(follower));
        Assert.Equal(frame, next.Frame(follower, 2));
        Assert.Equal(trails, next.Trails);
        UnitVisualState Second(double t) => March(t - 1000) with { CellX = State.CellX + 1 + MapMovementCadence.Progress(t - 1000) };
        Advance(next, 1000, Second, catalog);
        Assert.InRange(next.Position(follower).X - point.X, 0, 3.200001);
        Assert.InRange(next.Position(follower).Y - point.Y, 0, 1.600001);
        Assert.Contains(next.Trails, trail => trail.Emitted == 1000);
        Advance(next, 1100, Second, catalog);
        Assert.Equal(point, playback.Position(follower));
        Assert.Equal(frame, playback.Frame(follower, 2));
        Assert.Equal(trails, playback.Trails);
    }

    [Fact]
    public void MoraleChangesKeepQueuedPositionsAndTimersAndNeverRelocateEmittedTrails()
    {
        var catalog = OverlappingCatalog with { Bodies = TrailCatalog.Bodies };
        UnitVisualState History(double t) => March(t) with
            { Morale = t < 900 ? 0 : 100, Condition = t < 900 ? UnitCondition.Normal : UnitCondition.Routed };
        var playback = new UnitMotionPlayback();
        var follower = catalog.Formations[0].Layouts[1].Members[1];
        Advance(playback, 899, History, catalog);
        var old = playback.Position(follower);
        double interval = playback.Interval(follower);
        var trails = playback.Trails.Where(t => t.Emitted == 800).ToArray();
        Advance(playback, 900, History, catalog);
        Assert.Equal(old.X + 3.2, playback.Position(follower).X, 6);
        Assert.Equal(interval, playback.Interval(follower));
        Assert.Equal(trails, playback.Trails.Where(t => t.Emitted == 800));
        var atBoundary = playback.Position(follower);
        Advance(playback, 999, History, catalog);
        Assert.Equal(atBoundary, playback.Position(follower));
        Advance(playback, 1000, History, catalog);
        Assert.DoesNotContain(playback.Trails, t => t.Emitted == 800);
    }

    [Fact]
    public void DayBoundaryDoesNotResetStationaryFormationSway()
    {
        var state = State with { Soldiers = 1001, Morale = 25 };
        var playback = new UnitMotionPlayback();
        Advance(playback, 1900, _ => state);
        var before = UnitVisuals.Resolve(state, TrailCatalog, Clips, playback);
        playback.Begin(state, TrailCatalog, 1900, 1900, false);
        Assert.Equal(before.Animations, UnitVisuals.Resolve(state, TrailCatalog, Clips, playback).Animations);
    }
}
