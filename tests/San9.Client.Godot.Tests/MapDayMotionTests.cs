using San9.Content.Maps;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MapDayMotionTests
{
    [Fact]
    public void MovementFollowsRecordedCornersAndClampsAtBothEnds()
    {
        var motion = new MapUnitMotion([new(new(190, 1, 1), 42, new(0, 0), new(10, 10), 20, NavigationStatus.Arrived)
        { Steps = [new(0, 0), new(10, 0), new(10, 10)] }]);
        Assert.Equal((0d, 0d), (motion.Sample(-1).X, motion.Sample(-1).Y));
        Assert.Equal((10d, 0d), (motion.Sample(.5).X, motion.Sample(.5).Y));
        Assert.Equal((10d, 5d), (motion.Sample(.75).X, motion.Sample(.75).Y));
        Assert.Equal((10d, 10d), (motion.Sample(2).X, motion.Sample(2).Y));
        Assert.NotEqual(motion.Sample(.25).Direction, motion.Sample(.75).Direction);
    }

    [Fact]
    public void ArrowFlightUsesDirectionalFramesAndFiniteStaggeredVolleys()
    {
        var art = new MapProjectile(Enumerable.Range(0, 16).Select(i => $"arrow-{i}").ToArray(), 64, true, 0, 0);
        var attack = new MapAttackMotion(1, false, 2, false, 0, 0, 100, 0, 1, UnitVisualAction.Ranged, MapProjectileKind.Arrow);
        Assert.Empty(MapProjectiles.Sample(attack, art, -1));
        var first = Assert.Single(MapProjectiles.Sample(attack, art, 0));
        Assert.Equal(0, first.X);
        var flying = MapProjectiles.Sample(attack, art, 100).First();
        Assert.True(flying.Y < 0);
        Assert.NotEqual(first.AssetId, MapProjectiles.Sample(attack, art, 300).First().AssetId);
        Assert.Empty(MapProjectiles.Sample(attack, art, MapProjectiles.Duration(attack)));
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
