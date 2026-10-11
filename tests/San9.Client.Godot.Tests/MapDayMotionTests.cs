using San9.Content.Maps;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MapDayMotionTests
{
    [Fact]
    public void EachDisplayPositionIsHeldUntilTheNextCallbackAndTheTenthArrivesAt900Milliseconds()
    {
        var motion = new MapUnitMotion([new(new(190, 1, 1), 42, new(0, 0), new(100, -100), 20, NavigationStatus.Arrived)
            { Steps = [new(100, 0), new(100, -100)] }]);
        Assert.Equal((0d, 0d), (motion.Sample(-1).X, motion.Sample(-1).Y));
        for (int step = 0; step < 10; step++)
        {
            var start = motion.Sample(step * 100);
            Assert.Equal(((step + 1) * 10d, -(step + 1) * 10d), (start.X, start.Y));
            Assert.Equal(start, motion.Sample(step * 100 + 99.999));
            Assert.Equal(UnitVisuals.Direction(new(0, 0), new(100, -100)), start.Direction);
        }
        Assert.Equal(motion.Sample(900), motion.Sample(1000));
    }

    [Fact]
    public void VisibilityUsesRecentDisplaySegmentInsteadOfLogicalCorners()
    {
        var motion = new MapUnitMotion([new(new(190, 1, 1), 42, new(0, 0), new(10, 10), 20, NavigationStatus.Arrived)
        { Steps = [new(10, 0)] }]);
        Assert.Equal(new[] { (3d, 3d), (8d, 8d) }, motion.Segment(250, 750));
        Assert.Equal(new[] { (10d, 10d), (10d, 10d) }, motion.Segment(1000, 2000));
        Assert.Equal(new[] { (0d, 0d), (3d, 3d) }, motion.Segment(-1, 250));
    }

    [Fact]
    public void WaypointEventsUseOneDailyDisplacementAndReturningToStartDoesNotMove()
    {
        UnitMovedEvent first = new(new(190, 1, 1), 42, new(0, 0), new(10, 0), 10, NavigationStatus.Arrived);
        UnitMovedEvent second = new(new(190, 1, 1), 42, new(10, 0), new(10, 10), 10, NavigationStatus.Arrived);
        var motion = new MapUnitMotion([first, second]);
        Assert.Equal((5d, 5d), (motion.Sample(400).X, motion.Sample(400).Y));
        var stationary = new MapUnitMotion([first, second with { To = first.From }]);
        Assert.False(stationary.HasDisplacement);
        Assert.Equal((0d, 0d), (stationary.Sample(400).X, stationary.Sample(400).Y));
    }

    [Fact]
    public void ArrowFlightUsesDirectionalFramesAndFiniteStaggeredVolleys()
    {
        var art = new MapProjectile(Enumerable.Range(0, 16).Select(i => $"arrow-{i}").ToArray(), 64, true, 0, 0);
        var attack = new MapAttackMotion(1, false, 2, true, 0, 0, 100, 0, 1, UnitVisualAction.Ranged, MapProjectileKind.Arrow);
        Assert.Empty(MapProjectiles.Sample(attack, art, -1));
        var first = Assert.Single(MapProjectiles.Sample(attack, art, 0));
        Assert.Equal(0, first.X);
        var flying = MapProjectiles.Sample(attack, art, 100).First();
        Assert.True(flying.Y < 0);
        Assert.Equal(50000, first.Depth);
        Assert.Equal(first.Depth, flying.Depth);
        Assert.NotEqual(first.AssetId, MapProjectiles.Sample(attack, art, 300).First().AssetId);
        Assert.Empty(MapProjectiles.Sample(attack, art, MapProjectiles.Duration(attack)));
    }

    [Fact]
    public void SiegeVolleyKeepsFastScatteredFlightsStableAcrossSamples()
    {
        var attack = new MapAttackMotion(1, false, 2, true, 0, 0, 400, 0, 1, UnitVisualAction.Ranged, MapProjectileKind.Arrow);
        var art = new MapProjectile(["arrow"], 64, false, 0, 0);
        var flying = MapProjectiles.Sample(attack, art, 200).ToArray();

        Assert.Equal(2, flying.Length);
        Assert.Equal(flying, MapProjectiles.Sample(attack, art, 200));
        Assert.InRange(flying[1].X, -16, 16);
        Assert.InRange(flying[1].Y, -8, 8);
        Assert.InRange(MapProjectiles.Duration(attack) - 800, 700, 900);
        Assert.Empty(MapProjectiles.Sample(attack, art, MapProjectiles.Duration(attack)));
    }

    [Theory]
    [InlineData(MapProjectileKind.Arrow)]
    [InlineData(MapProjectileKind.Stone)]
    public void ArrowAndStoneUseFastStableArcs(MapProjectileKind kind)
    {
        var attack = new MapAttackMotion(1, false, 2, true, 0, 0, 400, 0, 1, UnitVisualAction.Ranged, kind)
        { Origins = [new(0, 0)] };
        var art = new MapProjectile(["projectile"], 64, false, 0, 0);
        double duration = MapProjectiles.Duration(attack);

        Assert.InRange(duration, 700, 900);
        var midpoint = Assert.Single(MapProjectiles.Sample(attack, art, duration / 2));
        Assert.True(midpoint.Y < 0);
        Assert.Equal(midpoint, Assert.Single(MapProjectiles.Sample(attack, art, duration / 2)));
        Assert.Empty(MapProjectiles.Sample(attack, art, duration));
    }

    [Fact]
    public void RockfallUsesGroundPathAndPlaysImpactOnlyAfterArrival()
    {
        var art = new MapProjectile(["rock0", "rock1", "rock2", "rock3"], 64, false, 0, 0)
        { Impact = new("impact", -10, -5) };
        var clips = new Dictionary<string, MapAnimationClip> { ["impact"] = new("impact", ["dust0", "dust1", "dust2", "dust3"], 100, []) };
        var attack = new MapAttackMotion(1, true, 2, false, 0, 0, 100, 100, 1, UnitVisualAction.Ranged, MapProjectileKind.Rockfall)
        { Path = [new(0, 0), new(100, 0), new(100, 100)] };
        double end = MapProjectiles.Duration(attack);
        var corner = Assert.Single(MapProjectiles.Sample(attack, art, end / 2, clips));
        Assert.Equal(100, corner.X, 6); Assert.Equal(0, corner.Y, 6);
        var impact = Assert.Single(MapProjectiles.Sample(attack, art, end, clips));
        Assert.Equal("dust0", impact.AssetId); Assert.Equal(90, impact.X); Assert.Equal(95, impact.Y);
        Assert.Empty(MapProjectiles.Sample(attack, art, end + 400, clips));
    }

    [Fact]
    public void BuildingVolleyUsesAllWallOriginsAndWaitsForTheLastArrow()
    {
        var attack = new MapAttackMotion(1, true, 2, false, 0, 0, 100, 0, 1, UnitVisualAction.Ranged, MapProjectileKind.Arrow)
        { Origins = [new(-100, 0), new(0, -100)] };
        var art = new MapProjectile(["a"], 64, false, 0, 0);
        Assert.Equal(-100, Assert.Single(MapProjectiles.Sample(attack, art, 0)).X);
        Assert.Equal(2, MapProjectiles.Sample(attack, art, 200).Count());
        Assert.Empty(MapProjectiles.Sample(attack, art, MapProjectiles.Duration(attack)));
    }
}
