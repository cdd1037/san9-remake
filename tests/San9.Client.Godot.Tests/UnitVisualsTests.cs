using San9.Content.Maps;
using San9.Core.Domain;
using San9.Rendering;
using static San9.Client.Godot.Tests.UnitVisualFixture;

namespace San9.Client.Godot.Tests;

public sealed class UnitVisualsTests
{
    private static UnitVisual Resolve(UnitVisualState state) => Sample(state, Catalog);

    [Fact]
    public void UnitBodiesAndLabelsUseTheSameProjectionOnARectangularMap()
    {
        var original = Resolve(State);
        var rectangular = Resolve(State with { MapHeight = 183 });
        Assert.Equal(original.AnchorX - 544, rectangular.AnchorX);
        Assert.Equal(original.LabelX - 544, rectangular.LabelX);
        Assert.Equal(original.AnchorY, rectangular.AnchorY);
        Assert.All(original.Animations.Zip(rectangular.Animations), pair =>
        {
            Assert.Equal(pair.First.X - 544, pair.Second.X);
            Assert.Equal(pair.First.Y, pair.Second.Y);
        });
    }

    [Fact]
    public void TerminalPresentationShowsZeroSoldiersWhileRetainingTheLastLivingBodyLayout()
    {
        var living = State with { Soldiers = 1001 };
        var terminal = living with { Soldiers = 0, RetainedBodySoldiers = living.Soldiers };
        var playback = new UnitMotionPlayback();
        Sample(living, Catalog, playback: playback);
        var visual = Sample(terminal, Catalog, 1000, history: t => t < 500 ? living : terminal, playback: playback);
        Assert.Equal(0u, visual.Soldiers);
        Assert.Equal(2, visual.Animations.Count(a => a.IsBody));
        var paused = Sample(terminal, Catalog, 1000, history: t => t < 500 ? living : terminal,
            visualAge: _ => 2000, playback: playback);
        Assert.Equal(0u, paused.Soldiers); Assert.Equal(2, paused.Animations.Count(a => a.IsBody));
    }

    [Fact]
    public void StationarySwayAcrossTerrainBoundaryDoesNotMakeBodiesJump()
    {
        var squares = Enumerable.Range(0, 40000).Select(i => new MapSquare(0, i % 200 < 90 ? 1 : 3, 0, 0)).ToArray();
        var state = State with { CellX = 89.25, Soldiers = 1001, Morale = 0 };
        double Smooth(double x, double y) => MapGeometry.SampleElevation(x, y, 200, 200, squares);
        double Stepped(double x, double y) => MapGeometry.TrySquare(x, y, out int cx, out int cy, 200, 200) ? squares[cy * 200 + cx].Elevation : 1;
        var smoothPlayback = new UnitMotionPlayback();
        var steppedPlayback = new UnitMotionPlayback();
        MapAnimatedSprite Member(UnitMotionPlayback playback, double time, Func<double, double, double> height) =>
            Sample(state, Catalog, time, elevationAt: height, playback: playback).Animations.Where(a => a.IsBody).MaxBy(a => a.GroundX)!;
        int steppedJump = 0, smoothJump = 0;
        var previousStep = Member(steppedPlayback, 0, Stepped); var previousSmooth = Member(smoothPlayback, 0, Smooth);
        for (int time = 16; time <= 30000; time += 16)
        {
            var step = Member(steppedPlayback, time, Stepped); var smooth = Member(smoothPlayback, time, Smooth);
            steppedJump = Math.Max(steppedJump, Math.Abs(step.Y - previousStep.Y));
            smoothJump = Math.Max(smoothJump, Math.Abs(smooth.Y - previousSmooth.Y));
            Assert.Equal(step.Depth, smooth.Depth);
            previousStep = step; previousSmooth = smooth;
        }
        Assert.True(steppedJump >= 20, "The old cell lookup must reproduce the stationary jump.");
        Assert.InRange(smoothJump, 0, 1);
    }

    [Fact]
    public void LeaderKeepsBaselineFrameRateAndReceivesTheFirstPositionEvenBetweenFollowerTicks()
    {
        var state = State with { Soldiers = 1001, Morale = 0, IsMoving = true, Action = UnitVisualAction.Moving };
        UnitVisualState History(double time) => state with { CellX = state.CellX + MapMovementCadence.Progress(time) };
        var playback = new UnitMotionPlayback();
        var first = Sample(History(0), Catalog, 0, History, playback: playback);
        var leader = first.Animations.Where(a => a.IsBody).MinBy(a => a.GroundX)!;
        var follower = first.Animations.Where(a => a.IsBody).MaxBy(a => a.GroundX)!;
        var beforeTick = Sample(History(99), Catalog, 99, History, playback: playback);
        Assert.Equal(leader.Frame, beforeTick.Animations.Where(a => a.IsBody).MinBy(a => a.GroundX)!.Frame);
        var atTick = Sample(History(100), Catalog, 100, History, playback: playback);
        Assert.NotEqual(leader.Frame, atTick.Animations.Where(a => a.IsBody).MinBy(a => a.GroundX)!.Frame);
        Assert.Equal(follower.Frame, atTick.Animations.Where(a => a.IsBody).MaxBy(a => a.GroundX)!.Frame);
        var rigid = Sample(History(0), Catalog, 0);
        Assert.Equal(rigid.Animations.Where(a => a.IsBody).MinBy(a => a.GroundX)!.GroundX, leader.GroundX, 6);
        var healthy = new UnitMotionPlayback();
        healthy.Begin(state with { Morale = 100 }, Catalog, 0, 0, true);
        Assert.All(Catalog.Formations[0].Layouts[1].Members, member => Assert.Equal(100, healthy.Interval(member)));
    }

    [Fact]
    public void LayoutReorderingPreservesMemberPositionsAndAnimationPhase()
    {
        var state = State with { Soldiers = 1001, Morale = 20 };
        UnitVisualState History(double t) => state with { CellX = state.CellX + t / 100 };
        var reordered = Catalog with
        {
            Formations = Catalog.Formations.Select(f => f with
            { Layouts = f.Layouts.Select(l => l with { Members = l.Members.Reverse().ToArray() }).ToArray() }).ToArray()
        };
        Assert.Equal(Sample(History(1000), Catalog, 1000, History).Animations,
            Sample(History(1000), reordered, 1000, History).Animations);
    }

    [Fact]
    public void EachMemberUsesItsOwnTerrainHeightWithoutChangingDepth()
    {
        var state=State with { Soldiers=1001, Morale=100 };
        var flat=Sample(state,Catalog);
        double middle=MapGeometry.Center(89,64, 200).X;
        var slope=Sample(state,Catalog,elevationAt:(x,y)=>x<middle?1:3);
        var a=flat.Animations.Where(s=>s.IsBody).ToArray();
        var b=slope.Animations.Where(s=>s.IsBody).ToArray();
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].Depth,b[i].Depth);
            Assert.Equal(a[i].Y-(a[i].GroundX<middle?0:20),b[i].Y);
        }
    }

    [Fact]
    public void AttackChangesBodyFramesWhileWakeFlagsAndConditionRemainSeparateLayers()
    {
        var catalog = Catalog with
        {
            Bodies = [Catalog.Bodies[0] with
        { MeleeAttack = new([new(Enumerable.Range(0, 8).Select(d => $"imperial-{d}").ToArray())], MapProjectileKind.None, 0),
          Wake = new("influence", -29, -14) }, Catalog.Bodies[1]]
        };
        var unit = Sample(State with { Action = UnitVisualAction.Melee, Condition = UnitCondition.Confused }, catalog);
        Assert.Equal("influence", unit.Animations[0].ClipId);
        Assert.Equal("imperial-0", Assert.Single(unit.Animations, s => s.IsBody).ClipId);
        Assert.Contains(unit.Animations, a => a.ClipId == "unit-flag-0-cloth-0");
        Assert.Contains(unit.Animations, a => a.ClipId == "confused");
    }

    [Fact]
    public void FinePositionAndElevationShareTheBuildingProjection()
    {
        var center = MapGeometry.Center(89, 64, 200);
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
        var center = MapGeometry.Center(89, 64, 200);
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
        var state = State with { IsMoving = true, Action = UnitVisualAction.Moving, MovementVisionVisible = true, VisionAngleDegrees = 180, HasCityInfluence = false };
        var moving = Sample(state, catalog);
        Assert.Equal(8, moving.Animations.Count(a => a.Depth < 30000));
        Assert.DoesNotContain(Sample(state with { IsEngaged = true }, catalog).Animations, a => a.Depth < 30000);
        Assert.Contains(Sample(state with { Action = UnitVisualAction.Idle, IsMoving = false }, catalog).Animations, a => a.Depth < 30000);
        Assert.DoesNotContain(Sample(state with { MovementVisionVisible = false }, catalog).Animations, a => a.Depth < 30000);
        Assert.DoesNotContain(Sample(state with { Action = UnitVisualAction.Melee }, catalog).Animations, a => a.Depth < 30000);
    }

    [Fact]
    public void UnitEffectsHaveIndependentDepthBands()
    {
        var u = Sample(State with { Soldiers = 1001, HasCityInfluence = true }, Catalog);
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
        var side = MapGeometry.Center(13, 9, 200);
        Assert.True(UnitOcclusion.IsOccluded(side.X, side.Y, [city], 200, 200));
        Assert.False(UnitOcclusion.IsOccluded(side.X, side.Y, [battle], 200, 200));
        var courtyard = MapGeometry.Center(12, 12, 200);
        Assert.False(UnitOcclusion.IsOccluded(courtyard.X, courtyard.Y, [city], 200, 200));
        var shifted = UnitOccluder.FromGeographicArea(new(new(63, 63), new(25, 25)), 2, true);
        Assert.Equal((10d, 10d, 16d, 16d), (shifted.Left, shifted.Top, shifted.Right, shifted.Bottom));
    }

    [Fact]
    public void OcclusionMarksRearApproachButExcludesCityCourtyard()
    {
        UnitOccluder city = new(10,10,15,15,2,true);
        var rear = MapGeometry.Center(9,9, 200);
        Assert.True(UnitOcclusion.IsOccluded(rear.X,rear.Y,[city], 200, 200));
        var near = MapGeometry.Center(7,7, 200);
        Assert.False(UnitOcclusion.IsOccluded(near.X,near.Y + 15,[city], 200, 200));
        Assert.True(UnitOcclusion.IsOccluded(near.X,near.Y + 17,[city], 200, 200));
        var inside = MapGeometry.Center(11,11, 200);
        Assert.False(UnitOcclusion.IsOccluded(inside.X,inside.Y,[city], 200, 200));
        var front = MapGeometry.Center(15,15, 200);
        Assert.False(UnitOcclusion.IsOccluded(front.X,front.Y,[city], 200, 200));
        Assert.False(UnitOcclusion.IsOccluded(rear.X,rear.Y,[], 200, 200));
    }
}
