using San9.Content.Maps;
using San9.Core.Domain;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class UnitVisualsTests
{
    private static readonly Dictionary<string, MapAnimationClip> Clips = Enumerable.Range(0, 8).Select(d => $"body-{d}")
        .Concat(Enumerable.Range(0, 8).Select(d => $"imperial-{d}"))
        .Concat(Enumerable.Range(0, 8).SelectMany(d => new[] { $"unit-flag-{d}-pole-back", $"unit-flag-{d}-pole-front", $"unit-flag-{d}-cloth-0", $"unit-flag-{d}-glyph-0-0", $"unit-flag-{d}-glyph-1-0", $"unit-flag-{d}-glyph-0-2" }))
        .Concat(["flag-glyph-0-0", "flag-glyph-1-0", "flag-pole-back", "flag-cloth-0", "flag-glyph-0-2", "flag-pole-front", "confused", "high", "influence"])
        .ToDictionary(id => id, id => new MapAnimationClip(id, [id + "-0", id + "-1"], 100,
            id.StartsWith("unit-flag-") ? [] : new() { ["spring"] = [id + "-0", id + "-1"], ["summer"] = [id + "-0", id + "-1"],
                ["autumn"] = [id + "-winter-0", id + "-winter-1"], ["winter"] = [id + "-winter-0", id + "-winter-1"] }));
    private static readonly MapUnitCatalog Catalog = new(
        [new("body", Enumerable.Range(0, 8).Select(d => $"body-{d}").ToArray(), 18, 30) { ImperialBodyId = "imperial" },
         new("imperial", Enumerable.Range(0, 8).Select(d => $"imperial-{d}").ToArray(), 18, 30)],
        [new(10000, [new(1, [new("body", 0, 0)], new("influence", -40, -50)),
                    new(1001, [new("body", -12, 0), new("body", 12, 0)], new("influence", -44, -57))], -64, -70),
         new(20000, [new(1, [new("body", 0, 0), new("body", 12, 12)], new("influence", -40, -50))], -64, -70)],
        new() { [UnitCondition.Confused] = new("confused", -16, 0), [UnitCondition.HighSpirits] = new("high", -16, 0) }, 0);
    private static UnitVisualState State => new(10000, 89, 64, 1, 1000, 10000, UnitCondition.Normal, 0, false, 0, 2);
    private static UnitVisual Resolve(UnitVisualState state) => UnitVisuals.Resolve(state, Catalog, Clips);

    [Fact]
    public void ContinuousHistoryKeepsFollowersAndEmitsTrailsAfterTheFirstDay()
    {
        var catalog = Catalog with { Bodies = Catalog.Bodies.Select(b => b with
            { Trail = new("influence", 0, 0), TrailChance = 100 }).ToArray() };
        UnitVisualState History(double t) => State with { Soldiers = 1001, Morale = 0,
            Action = UnitVisualAction.Moving, CellX = State.CellX + t / 100 };
        var before = UnitVisuals.Resolve(History(999), catalog, Clips, 999, History);
        var after = UnitVisuals.Resolve(History(1000), catalog, Clips, 1000, History);
        var a = before.Animations.Where(s => s.IsBody).OrderBy(s => s.GroundX).ToArray();
        var b = after.Animations.Where(s => s.IsBody).OrderBy(s => s.GroundX).ToArray();
        Assert.All(a.Zip(b), pair => Assert.InRange(pair.Second.GroundX - pair.First.GroundX, 0, 1));
        var later = UnitVisuals.Resolve(History(1400), catalog, Clips, 1400, History);
        Assert.Contains(later.Animations, s => s.ClipId == "influence");
        var paused = UnitVisuals.Resolve(History(1400), catalog, Clips, 1400, History, t => 2400 - t);
        Assert.DoesNotContain(paused.Animations, s => s.ClipId == "influence");
    }

    [Fact]
    public void DayBoundaryDoesNotResetFormationSwayOrMoveOldTrails()
    {
        var state = State with { Soldiers = 1001, Morale = 25 };
        var before = UnitVisuals.Resolve(state, Catalog, Clips, 1900, positionMilliseconds: 7300);
        var after = UnitVisuals.Resolve(state, Catalog, Clips, 0, positionMilliseconds: 7300);
        Assert.Equal(before.Animations, after.Animations);
        var catalog = Catalog with { Bodies = Catalog.Bodies.Select(b => b with
            { Trail = new("influence", 0, 0), TrailChance = 100 }).ToArray() };
        UnitVisualState History(double t) => state with { Action = UnitVisualAction.Moving, CellX = state.CellX + t / 100 };
        var early = UnitVisuals.Resolve(History(90), catalog, Clips, 90, History, positionMilliseconds: 5090);
        var late = UnitVisuals.Resolve(History(110), catalog, Clips, 110, History, positionMilliseconds: 5110);
        Assert.Equal(early.Animations.Where(a => a.Frame is not null).Select(a => (a.X, a.Y)),
            late.Animations.Where(a => a.Frame is not null).Select(a => (a.X, a.Y)));
    }

    [Fact]
    public void LowMoraleFollowersDoNotAccelerateToArriveWithTheLeader()
    {
        var state=State with { Soldiers=1001, Morale=0, Action=UnitVisualAction.Moving };
        UnitVisualState History(double t) => state with { CellX=state.CellX+Math.Min(t,900)/100 };
        var arrival=UnitVisuals.Resolve(History(900),Catalog,Clips,900,History);
        var rigid=UnitVisuals.Resolve(History(900),Catalog,Clips,900);
        var delayed=arrival.Animations.Where(a=>a.IsBody).OrderBy(a=>a.ClipId).Select(a=>a.GroundX).ToArray();
        var targets=rigid.Animations.Where(a=>a.IsBody).OrderBy(a=>a.ClipId).Select(a=>a.GroundX).ToArray();
        Assert.True(delayed.Sum()<targets.Sum()-32);
        Assert.Equal(rigid.Animations.Count(a=>a.IsBody),delayed.Length);
    }

    [Fact]
    public void EachMemberUsesItsOwnTerrainHeightWithoutChangingDepth()
    {
        var state=State with { Soldiers=1001, Morale=100 };
        var flat=UnitVisuals.Resolve(state,Catalog,Clips);
        double middle=MapGeometry.Center(89,64).X;
        var slope=UnitVisuals.Resolve(state,Catalog,Clips,elevationAt:(x,y)=>x<middle?1:3);
        var a=flat.Animations.Where(s=>s.IsBody).ToArray();
        var b=slope.Animations.Where(s=>s.IsBody).ToArray();
        for(int i=0;i<a.Length;i++) {
            Assert.Equal(a[i].Depth,b[i].Depth);
            Assert.Equal(a[i].Y-(a[i].GroundX<middle?0:20),b[i].Y);
        }
    }

    [Fact]
    public void PausedMovementKeepsBodyPositionsWhileExistingTrailsExpire()
    {
        var catalog = Catalog with { Bodies=Catalog.Bodies.Select(b => b with { Trail=new("influence",0,0), TrailChance=100 }).ToArray() };
        var state = State with { Morale=50, Action=UnitVisualAction.Moving };
        UnitVisualState History(double t) => state with { CellX=state.CellX+t/100 };
        var before=UnitVisuals.Resolve(History(180),catalog,Clips,180,History);
        var paused=UnitVisuals.Resolve(History(180),catalog,Clips,180,History,t=>1180-t);
        Assert.Contains(before.Animations,a=>a.Frame is not null);
        Assert.DoesNotContain(paused.Animations,a=>a.Frame is not null);
        Assert.Equal(before.Animations.Where(a=>a.IsBody),paused.Animations.Where(a=>a.IsBody));
    }

    [Fact]
    public void AttackChangesBodyFramesWhileWakeFlagsAndConditionRemainSeparateLayers()
    {
        var catalog = Catalog with { Bodies = [Catalog.Bodies[0] with
        { MeleeActions = [new(Enumerable.Range(0, 8).Select(d => $"imperial-{d}").ToArray(), MapProjectileKind.None)],
          Wake = new("influence", -29, -14) }, Catalog.Bodies[1]] };
        var unit = UnitVisuals.Resolve(State with { Action = UnitVisualAction.Melee, Condition = UnitCondition.Confused }, catalog, Clips);
        Assert.Equal("influence", unit.Animations[0].ClipId);
        Assert.Equal("imperial-0", Assert.Single(unit.Animations, s => s.IsBody).ClipId);
        Assert.Contains(unit.Animations, a => a.ClipId == "unit-flag-0-cloth-0");
        Assert.Contains(unit.Animations, a => a.ClipId == "confused");
    }

    [Fact]
    public void FinePositionAndElevationShareTheBuildingProjection()
    {
        var center = MapGeometry.Center(89, 64);
        var unit = Resolve(State);
        Assert.Equal(center.X - 18, unit.Animations[0].X);
        Assert.Equal(center.Y - 30, unit.Animations[0].Y);
        var moved = Resolve(State with { CellX = 89.2, Elevation = 2 });
        Assert.Equal(unit.Animations[0].X + 6, moved.Animations[0].X);
        Assert.Equal(unit.Animations[0].Y + 3 - 10, moved.Animations[0].Y);
        Assert.True(moved.Depth > unit.Depth);
    }

    [Fact]
    public void InformationCardStaysAboveTheUnitAndPreservesCommanderAndActualValues()
    {
        var unit = Resolve(State with { Soldiers = 18000, Morale = 85, CommanderNameKey = "officer.commander" });
        Assert.Equal("officer.commander", unit.CommanderNameKey);
        Assert.Equal(18000u, unit.Soldiers);
        Assert.Equal(85, unit.Morale);
        Assert.True(unit.LabelY + 47 < unit.Animations[0].Y);
        var changed = Resolve(State with { Soldiers = 1, Condition = UnitCondition.Confused, Direction = 4 });
        Assert.Equal((unit.LabelX, unit.LabelY), (changed.LabelX, changed.LabelY));
    }

    [Fact]
    public void SoldierThresholdsAndFormationReferencesChangeTheVisibleRanks()
    {
        Assert.Single(Resolve(State).Animations, s => s.ClipId.StartsWith("body-"));
        var large = Resolve(State with { Soldiers = 1001 });
        Assert.Equal(2, large.Animations.Count(s => s.ClipId.StartsWith("body-")));
        Assert.NotEqual(large.Animations, Resolve(State with { Soldiers = 1001, FormationId = 20000 }).Animations);
        var enormous = Resolve(State with { Soldiers = uint.MaxValue });
        Assert.Equal(large.Animations, enormous.Animations);
        Assert.Equal(uint.MaxValue, enormous.Soldiers);
    }

    [Fact]
    public void InfluenceAndConditionCoexistAndClearIndependently()
    {
        var confused = Resolve(State with { HasCityInfluence = true, Condition = UnitCondition.Confused });
        Assert.All(confused.Animations.Where(s => s.IsBody), s => Assert.StartsWith("body-", s.ClipId));
        Assert.DoesNotContain(confused.Animations.Where(s => !s.IsBody), s => s.ClipId.StartsWith("body-"));
        Assert.Contains(confused.Animations, s => s.ClipId == "confused");
        Assert.Contains(confused.Animations, s => s.ClipId == "influence");
        var recovered = Resolve(State with { HasCityInfluence = true });
        Assert.DoesNotContain(recovered.Animations, s => s.ClipId == "confused");
        Assert.Contains(recovered.Animations, s => s.ClipId == "influence");
        Assert.DoesNotContain(Resolve(State with { Condition = UnitCondition.HighSpirits }).Animations, s => s.ClipId == "influence");
    }

    [Fact]
    public void IdleAndFlagsAnimateWithoutSimulationChangesAndKeepSeasonalPhase()
    {
        var unit = Resolve(State with { Glyph = -1, Direction = 6 });
        Assert.DoesNotContain(unit.Animations, s => s.ClipId.Contains("glyph"));
        Assert.Equal("body-6", unit.Animations[0].ClipId);
        foreach (var sprite in unit.Animations)
        {
            var clip = Clips[sprite.ClipId];
            Assert.NotEqual(MapAnimationClock.Asset(clip, "spring", 0, sprite.Phase), MapAnimationClock.Asset(clip, "spring", 100, sprite.Phase));
            if (sprite.ClipId.StartsWith("unit-flag-"))
                Assert.Equal(MapAnimationClock.Asset(clip, "spring", 100, sprite.Phase), MapAnimationClock.Asset(clip, "winter", 100, sprite.Phase));
            else Assert.Contains("winter", MapAnimationClock.Asset(clip, "winter", 100, sprite.Phase));
        }
    }

    [Fact]
    public void MissingBodiesAndClipsFailValidation()
    {
        Catalog.Validate(Clips);
        Assert.Throws<InvalidDataException>(() => (Catalog with { Bodies = [] }).Validate(Clips));
        Assert.Throws<InvalidDataException>(() => Catalog.Validate(new Dictionary<string, MapAnimationClip>()));
    }

    [Fact]
    public void CompleteLayoutsReplaceRanksAndShareTheirInfluenceAnchor()
    {
        var small = Resolve(State with { HasCityInfluence = true });
        var large = Resolve(State with { Morale = 100, Soldiers = 1001, HasCityInfluence = true, Direction = 6 });
        var center = MapGeometry.Center(89, 64);
        var soldiers = large.Animations.Where(s => s.ClipId.StartsWith("body-")).ToArray();
        Assert.Equal(new[] { center.X - 30, center.X - 6 }, soldiers.Select(s => s.X));
        Assert.All(soldiers, s => Assert.Equal(center.Y - 30, s.Y));
        Assert.DoesNotContain(soldiers, s => s.X == center.X - 18);
        Assert.Equal(center.X - 40, small.Animations.Single(s => s.ClipId == "influence").X);
        Assert.Equal(center.X - 44, large.Animations.Single(s => s.ClipId == "influence").X);
        Assert.Equal(center.Y - 57, large.Animations.Single(s => s.ClipId == "influence").Y);
    }

    [Fact]
    public void ImperialBodiesAndRulerFlagsAreIndependentAndReversible()
    {
        var imperial = Resolve(State with { UsesImperialAppearance = true });
        Assert.Contains(imperial.Animations, s => s.ClipId == "imperial-0");
        Assert.Contains(imperial.Animations, s => s.ClipId == "unit-flag-0-glyph-0-2");
        var ruler = Resolve(State with { IsRulerCommander = true });
        Assert.Contains(ruler.Animations, s => s.ClipId == "body-0");
        Assert.Contains(ruler.Animations, s => s.ClipId == "unit-flag-0-glyph-0-0");
        Assert.DoesNotContain(Resolve(State).Animations, s => s.ClipId == "unit-flag-0-glyph-0-0" || s.ClipId.StartsWith("imperial-"));
    }

    [Fact]
    public void UnitFlagsFollowAllEightFacingsWithoutChangingForTheSeason()
    {
        foreach (int direction in Enumerable.Range(0, 8))
        {
            var unit = Resolve(State with { Direction = direction });
            var flags = unit.Animations.Where(s => s.ClipId.StartsWith("unit-flag-")).ToArray();
            Assert.Equal(4, flags.Length);
            Assert.All(flags, flag =>
            {
                Assert.StartsWith($"unit-flag-{direction}-", flag.ClipId);
                var clip = Clips[flag.ClipId];
                Assert.Empty(clip.SeasonFrames);
                Assert.Equal(MapAnimationClock.Asset(clip, "spring", 142), MapAnimationClock.Asset(clip, "winter", 142));
            });
        }
        Assert.Throws<InvalidDataException>(() => Catalog.Validate(Clips.Where(p => p.Key != "unit-flag-7-pole-front").ToDictionary()));
    }

    [Theory]
    [InlineData(0, 1, 4)]
    [InlineData(1, 0, 2)]
    [InlineData(0, -1, 0)]
    [InlineData(-1, 0, 6)]
    public void FacingUsesTheCurrentRouteOrTarget(int x, int y, int expected) =>
        Assert.Equal(expected, UnitVisuals.Direction(new(10, 10), new(10 + x, 10 + y)));

    [Fact]
    public void MovingVisionUsesAngleAndDisappearsInCombat()
    {
        var catalog = Catalog with { VisionSlices = Enumerable.Range(0,16).Select(_ => new MapUnitEffect("influence",0,0)).ToArray() };
        var state = State with { Action = UnitVisualAction.Moving, VisionAngleDegrees = 180, HasCityInfluence = false };
        var moving = UnitVisuals.Resolve(state, catalog, Clips);
        Assert.Equal(8, moving.Animations.Count(a => a.Depth < 30000));
        Assert.DoesNotContain(UnitVisuals.Resolve(state with { IsEngaged = true }, catalog, Clips).Animations, a => a.Depth < 30000);
        Assert.DoesNotContain(UnitVisuals.Resolve(state with { Action = UnitVisualAction.Idle }, catalog, Clips).Animations, a => a.Depth < 30000);
    }

    [Fact]
    public void MembersFollowHistoryAndCatchUpWithoutChangingAuthoritativeState()
    {
        var state = State with { Soldiers = 1001, Morale = 50, Action = UnitVisualAction.Moving };
        UnitVisualState History(double t) => state with { CellX = state.CellX + Math.Min(t,900) / 900 * 4 };
        var middle = UnitVisuals.Resolve(History(450), Catalog, Clips, 450, History);
        var repeated = UnitVisuals.Resolve(History(450), Catalog, Clips, 450, History);
        Assert.Equal(middle.Animations, repeated.Animations);
        var end = UnitVisuals.Resolve(History(1800), Catalog, Clips, 1800, History);
        var stationaryEnd = UnitVisuals.Resolve(History(1800), Catalog, Clips, 1800);
        Assert.Equal(end.Animations, stationaryEnd.Animations);
        var rigid = UnitVisuals.Resolve(History(450), Catalog, Clips, 450);
        Assert.NotEqual(rigid.Animations.Where(a => a.IsBody).Last().X, middle.Animations.Where(a => a.IsBody).Last().X);
    }

    [Fact]
    public void TrailRemainsAtEmissionPointAndExpiresAfterMovement()
    {
        var catalog = Catalog with { Bodies = Catalog.Bodies.Select(b => b with { Trail = new("influence",0,0), TrailChance = 100 }).ToArray() };
        var state = State with { Morale = 100, HasCityInfluence = false };
        UnitVisualState History(double t) => state with { CellX = state.CellX + Math.Min(900,t) / 100,
            Action = t < 900 ? UnitVisualAction.Moving : UnitVisualAction.Idle };
        var first = UnitVisuals.Resolve(History(20), catalog, Clips, 20, History).Animations.Single(a => a.Frame is not null);
        var next = UnitVisuals.Resolve(History(120), catalog, Clips, 120, History).Animations.First(a => a.Frame is not null);
        Assert.Equal((first.X, first.Y), (next.X, next.Y));
        Assert.NotEqual(first.Frame, next.Frame);
        Assert.DoesNotContain(UnitVisuals.Resolve(History(1900), catalog, Clips, 1900, History).Animations, a => a.Frame is not null);
    }

    [Fact]
    public void UnitEffectsHaveIndependentDepthBands()
    {
        var u = UnitVisuals.Resolve(State with { Soldiers = 1001, HasCityInfluence = true }, Catalog, Clips);
        double flag = u.Animations.First(a => a.ClipId.Contains("pole-back")).Depth;
        Assert.True(u.Animations.Single(a => a.ClipId == "influence").Depth < flag);
        Assert.All(u.Animations.Where(a => a.IsBody), a => Assert.Equal(a.GroundY + 30000, a.Depth));
    }

    [Fact]
    public void CitySideWallUsesFullGeographicCoverageAndQuantizesOutward()
    {
        var position = MapCoordinates.Center(new(12, 12));
        var city = UnitOccluder.FromGeographicArea(new(position, new(25, 25)), 2, true);
        var battle = UnitOccluder.FromGeographicArea(new(position, new(5, 5)), 2, true);
        var side = MapGeometry.Center(13, 9);
        Assert.True(UnitOcclusion.IsOccluded(side.X, side.Y, [city]));
        Assert.False(UnitOcclusion.IsOccluded(side.X, side.Y, [battle]));
        var courtyard = MapGeometry.Center(12, 12);
        Assert.False(UnitOcclusion.IsOccluded(courtyard.X, courtyard.Y, [city]));
        var shifted = UnitOccluder.FromGeographicArea(new(new(63, 63), new(25, 25)), 2, true);
        Assert.Equal((10d, 10d, 16d, 16d), (shifted.Left, shifted.Top, shifted.Right, shifted.Bottom));
    }

    [Fact]
    public void OcclusionMarksRearApproachButExcludesCityCourtyard()
    {
        UnitOccluder city = new(10,10,15,15,2,true);
        var rear = MapGeometry.Center(9,9);
        Assert.True(UnitOcclusion.IsOccluded(rear.X,rear.Y,[city]));
        var near = MapGeometry.Center(7,7);
        Assert.False(UnitOcclusion.IsOccluded(near.X,near.Y + 15,[city]));
        Assert.True(UnitOcclusion.IsOccluded(near.X,near.Y + 17,[city]));
        var inside = MapGeometry.Center(11,11);
        Assert.False(UnitOcclusion.IsOccluded(inside.X,inside.Y,[city]));
        var front = MapGeometry.Center(15,15);
        Assert.False(UnitOcclusion.IsOccluded(front.X,front.Y,[city]));
        Assert.False(UnitOcclusion.IsOccluded(rear.X,rear.Y,[]));
    }
}
